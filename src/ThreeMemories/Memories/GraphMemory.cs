using AgentMemory;
using AgentMemory.Abstractions.Options;
using AgentMemory.Abstractions.Services;
using AgentMemory.AgentFramework;
using AgentMemory.Core.Stubs;
using AgentMemory.Neo4j.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace ThreeMemories.Memories;

/// <summary>
/// The graph memory: AgentMemory for .NET (https://github.com/joslat/agent-memory-dotnet) on Neo4j.
/// A model reads each statement and writes people, places, events, facts and how they relate into a
/// graph; recall searches that graph by meaning and then follows its relationships.
/// </summary>
/// <remarks>
/// Configured exactly as the library's own ConversationalAgent sample: the Conversational preset on
/// each package and nothing tuned by hand. The one change is that statements are learned inline
/// instead of in the background, so that each is stamped with the moment the script says it was said.
/// </remarks>
public sealed class GraphMemory : IMemory
{
    private readonly IHost _host;
    private readonly AsyncServiceScope _scope;
    private readonly Neo4jMicrosoftMemoryFacade _memory;
    private readonly Neo4jConnection _connection;
    private readonly FileLog _log;
    private readonly string _owner;

    private GraphMemory(IHost host, AsyncServiceScope scope, Neo4jConnection connection, FileLog log, string owner)
    {
        _host = host;
        _scope = scope;
        _memory = scope.ServiceProvider.GetRequiredService<Neo4jMicrosoftMemoryFacade>();
        _connection = connection;
        _log = log;
        _owner = owner;
    }

    public string Name => "graph";

    public string How => "AgentMemory for .NET on Neo4j: similarity first, then the graph's relationships";

    /// <summary>Whose memories these are. A new one per run, so runs never see each other.</summary>
    public string Owner => _owner;

    /// <summary>How many extracted items the library did not store, and the log that names them.</summary>
    public (int Count, string Path) Skipped => (_log.Skipped, _log.Path);

    public static async Task<GraphMemory> CreateAsync(
        Models models,
        IClock clock,
        Neo4jConnection connection,
        string owner,
        string logPath,
        CancellationToken cancellationToken = default)
    {
        var log = new FileLog(logPath);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(models.Chat);          // the model that reads what was said
        builder.Services.AddSingleton(models.Embeddings);
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        builder.Services.AddNeo4jAgentMemory(
            MemoryOptions.CreateConversational(),
            neo4j =>
            {
                neo4j.Uri = connection.Uri;
                neo4j.Username = connection.Username;
                neo4j.Password = connection.Password;
                // The vector index is built for one width, so it has to be the embedding model's.
                neo4j.EmbeddingDimensions = models.Settings.EmbeddingDimensions
                    ?? throw new InvalidOperationException("The embedding model's dimensions are unknown.");
            },
            llm => llm.ApplyConversational());

        builder.Services.AddAgentMemoryFramework(options =>
        {
            options.ApplyConversational();
            options.ExtractInBackground = false;
        });

        var host = builder.Build();
        var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISchemaBootstrapper>().BootstrapAsync(cancellationToken);

        return new GraphMemory(host, scope, connection, log, owner);
    }

    public Task RememberAsync(Turn turn, CancellationToken cancellationToken = default)
    {
        var session = SessionId(turn.Session);
        return _memory.PersistAfterRunAsync(
            [new ChatMessage(ChatRole.User, turn.Text)], session, session, _owner, cancellationToken);
    }

    public async Task<string> RecallAsync(string question, int? budgetTokens, CancellationToken cancellationToken = default)
    {
        // A new conversation, as it would be weeks later: nothing of what was said is "recent".
        var session = SessionId("ask");
        var recalled = await _memory.GetContextForRunAsync(
            [new ChatMessage(ChatRole.User, question)], session, session, _owner, cancellationToken);

        return string.Join("\n\n", recalled.Select(message => message.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>What the graph holds for this owner, read straight from Neo4j.</summary>
    public async Task<GraphSnapshot> SnapshotAsync()
    {
        await using var driver = GraphDatabase.Driver(
            _connection.Uri, AuthTokens.Basic(_connection.Username, _connection.Password));

        var entities = await RowsAsync(driver, """
            MATCH (e:Entity {owner_id: $owner})
            RETURN e.name AS name, coalesce(e.type, '') AS type
            ORDER BY type, name
            """, row => new GraphEntity(row["name"].As<string>(), row["type"].As<string>()));

        var facts = await RowsAsync(driver, """
            MATCH (f:Fact {owner_id: $owner})
            WHERE f.invalidated_at IS NULL
            RETURN f.subject AS subject, f.predicate AS predicate, f.object AS object,
                   coalesce(toString(f.occurred_on), toString(f.valid_from), '') AS since,
                   coalesce(toString(f.valid_until), '') AS until
            ORDER BY f.created_at
            """, row => new GraphFact(
                row["subject"].As<string>(), row["predicate"].As<string>(), row["object"].As<string>(),
                row["since"].As<string>(), row["until"].As<string>()));

        var relationships = await RowsAsync(driver, """
            MATCH (a:Entity {owner_id: $owner})-[r:RELATED_TO]->(b:Entity)
            RETURN a.name AS source, coalesce(r.relation_type, 'RELATED_TO') AS relation, b.name AS target
            ORDER BY source, target
            """, row => new GraphRelationship(
                row["source"].As<string>(), row["relation"].As<string>(), row["target"].As<string>()));

        var preferences = await RowsAsync(driver, """
            MATCH (p:Preference {owner_id: $owner})
            RETURN coalesce(p.category, '') AS category, p.preference AS preference
            ORDER BY category
            """, row => new GraphPreference(row["category"].As<string>(), row["preference"].As<string>()));

        return new GraphSnapshot(entities, facts, relationships, preferences);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        if (_host is IAsyncDisposable host) await host.DisposeAsync();
        else _host.Dispose();
        _log.Dispose();
    }

    private string SessionId(string session) => $"{_owner}-{session}";

    private async Task<IReadOnlyList<T>> RowsAsync<T>(IDriver driver, string cypher, Func<IRecord, T> read)
    {
        var result = await driver.ExecutableQuery(cypher).WithParameters(new { owner = _owner }).ExecuteAsync();
        return result.Result.Select(read).ToList();
    }
}

/// <summary>Where the Neo4j of <c>compose.yaml</c> is, unless the environment says otherwise.</summary>
public sealed record Neo4jConnection(string Uri, string Username, string Password)
{
    public static Neo4jConnection FromEnvironment() => new(
        Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687",
        Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j",
        Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "password");
}

public sealed record GraphSnapshot(
    IReadOnlyList<GraphEntity> Entities,
    IReadOnlyList<GraphFact> Facts,
    IReadOnlyList<GraphRelationship> Relationships,
    IReadOnlyList<GraphPreference> Preferences);

public sealed record GraphEntity(string Name, string Type);

public sealed record GraphFact(string Subject, string Predicate, string Object, string Since, string Until);

public sealed record GraphRelationship(string Source, string Relation, string Target);

public sealed record GraphPreference(string Category, string Preference);
