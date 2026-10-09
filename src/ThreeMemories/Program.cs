using System.Globalization;
using System.Text;
using ThreeMemories;
using ThreeMemories.Memories;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var root = FindRepositoryRoot();
var scenarioPath = Argument("--scenario") ?? Path.Combine(root, "scenarios", "go-live.json");
var only = (Argument("--only") ?? "file,vector,graph").Split(',', StringSplitOptions.TrimEntries);
var fallbackBudget = int.Parse(Argument("--budget") ?? "300", CultureInfo.InvariantCulture);
var results = Directory.CreateDirectory(Path.Combine(root, "results")).FullName;

var scenario = Scenario.Load(scenarioPath);
var models = Models.FromEnvironment();
var clock = new ScenarioClock();

Console.WriteLine();
Console.WriteLine("THREE MEMORIES, ONE QUESTION");
Console.WriteLine($"  scenario  {scenario.Name}: {scenario.Sessions.Sum(s => s.Messages.Count)} statements in {scenario.Sessions.Count} sessions, " +
                  $"{scenario.Sessions[0].Date:MMM d} to {scenario.Sessions[^1].Date:MMM d, yyyy}");
Console.WriteLine($"  models    {models.Settings.Summary}");

// ── The three memories ──────────────────────────────────────────────────────
var notes = Path.Combine(results, "MEMORY.md");
File.Delete(notes);

var memories = new List<IMemory>();
if (only.Contains("file")) memories.Add(new FileMemory(notes));
if (only.Contains("vector")) memories.Add(new VectorMemory(models.Embeddings));
if (only.Contains("graph"))
{
    // A new owner per run: the graph of an earlier run is never what answers this one.
    var owner = $"{scenario.Owner}-{Guid.NewGuid():N}"[..(scenario.Owner.Length + 9)];
    var log = Path.Combine(results, "graph-memory.log");
    memories.Add(await GraphMemory.CreateAsync(models, clock, Neo4jConnection.FromEnvironment(), owner, log));
}

var experiment = new Experiment(scenario, models, clock, fallbackBudget);

// ── Writing: every memory hears the same statements ─────────────────────────
Console.WriteLine();
Console.WriteLine("WRITING  every memory hears the same statements");
var live = !Console.IsOutputRedirected;   // a progress line that rewrites itself only makes sense on a terminal
var written = await experiment.WriteAsync(memories, line => { if (live) Console.Write($"\r  {line,-92}"); });
if (live) Console.Write($"\r{new string(' ', 96)}\r");
foreach (var memory in written)
{
    Console.WriteLine($"  {memory.Memory,-7} {memory.Took.TotalSeconds,6:0.0} s   {memory.How}");
}

var graph = memories.OfType<GraphMemory>().FirstOrDefault();
var snapshot = graph is null ? null : await graph.SnapshotAsync();
if (snapshot is not null)
{
    Console.WriteLine($"  the graph now holds {snapshot.Entities.Count} entities, {snapshot.Facts.Count} facts, " +
                      $"{snapshot.Relationships.Count} relationships and {snapshot.Preferences.Count} preferences");
    var (skipped, where) = graph!.Skipped;
    if (skipped > 0)
    {
        Console.WriteLine($"  it extracted {skipped} more items that it did not store: {Path.GetRelativePath(Environment.CurrentDirectory, where)}");
    }
}

// ── Asking: same question, same room in the prompt ──────────────────────────
Console.WriteLine();
Console.WriteLine($"ASKING   on {scenario.AskedOn:dddd, MMMM d, yyyy}, in a new conversation");

var asked = new List<Asked>();
foreach (var question in scenario.Questions)
{
    var result = await experiment.AskAsync(question, memories);
    asked.Add(result);

    Console.WriteLine();
    Console.WriteLine($"  {question.Kind.ToUpperInvariant()}");
    Console.WriteLine($"  \"{question.Text}\"   (room in the prompt: {result.Budget} tokens each)");
    foreach (var attempt in result.Attempts)
    {
        var why = attempt.MissingFromPrompt.Count > 0
            ? $"never reached the prompt: {string.Join("; ", attempt.MissingFromPrompt.Select(need => need.Fact))}"
            : attempt.AnswerIsRight
                ? "had what it needed, and the answer is right"
                : "had what it needed, and the answer still missed the point";
        Console.WriteLine($"    {attempt.Verdict} {attempt.Memory,-7} {why}");
        Console.WriteLine($"        → {Flatten(attempt.Answer, 150)}");
    }
}

// ── The scoreboard ──────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("RESULT");
Console.WriteLine($"  {"",-8}{string.Join("", scenario.Questions.Select(q => $"{q.Id,-11}"))}");
foreach (var memory in memories)
{
    var row = asked.Select(a => a.Attempts.Single(attempt => attempt.Memory == memory.Name).Verdict);
    Console.WriteLine($"  {memory.Name,-8}{string.Join("", row.Select(verdict => $"{verdict,-11}"))}");
}

Console.WriteLine("  ✓ the facts reached the prompt and the answer is right");
Console.WriteLine("  ✗ a fact never reached the prompt, and nothing said so");
Console.WriteLine("  ~ the facts were in the prompt and the answer still missed the point");

var report = Path.Combine(results, "latest.md");
await File.WriteAllTextAsync(report, Report.Markdown(scenario, models, written, asked, snapshot, graph?.Owner));
Console.WriteLine();
Console.WriteLine($"  full report, with every prompt and every answer: {Path.GetRelativePath(Environment.CurrentDirectory, report)}");
if (graph is not null)
{
    Console.WriteLine($"  see the graph at http://localhost:7474 with:  MATCH (n {{owner_id: '{graph.Owner}'}}) RETURN n");
}

foreach (var memory in memories) await memory.DisposeAsync();
return;

string? Argument(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Flatten(string text, int length)
{
    var line = string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
    return line.Length <= length ? line : line[..(length - 1)] + "…";
}

// The folder that holds compose.yaml, so the demo runs the same from the root or from the project.
static string FindRepositoryRoot()
{
    for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "compose.yaml"))) return folder.FullName;
    }

    return Environment.CurrentDirectory;
}
