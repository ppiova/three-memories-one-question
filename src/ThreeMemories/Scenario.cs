using System.Text.Json;

namespace ThreeMemories;

/// <summary>
/// A scripted history (sessions of things one person said, over months) and the questions asked afterwards.
/// </summary>
public sealed record Scenario(
    string Name,
    string Title,
    string Owner,
    string AssistantBrief,
    DateOnly AskedOn,
    IReadOnlyList<Session> Sessions,
    IReadOnlyList<Question> Questions)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Scenario Load(string path) =>
        JsonSerializer.Deserialize<Scenario>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"'{path}' is not a scenario.");

    /// <summary>The moment the questions are asked: the morning of <see cref="AskedOn"/>.</summary>
    public DateTimeOffset AskedAt => new(AskedOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

    /// <summary>Every statement in the order it was said, each stamped with its own moment.</summary>
    public IEnumerable<Turn> Turns()
    {
        for (var s = 0; s < Sessions.Count; s++)
        {
            var session = Sessions[s];
            var start = new DateTimeOffset(session.Date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
            for (var m = 0; m < session.Messages.Count; m++)
            {
                yield return new Turn($"s{s + 1}", start.AddMinutes(m * 2), session.Messages[m]);
            }
        }
    }
}

public sealed record Session(DateOnly Date, string Topic, IReadOnlyList<string> Messages);

/// <summary>
/// A question, the facts the prompt must contain for it to be answerable, and what a right answer says.
/// </summary>
public sealed record Question(string Id, string Kind, string Text, IReadOnlyList<Need> Needs, string RightAnswer);

/// <summary>
/// One fact the prompt must contain. It is there when every group in <see cref="Mentions"/> has
/// at least one of its spellings in the text: [["Bariloche"], ["December 9", "2026-12-09"]] reads
/// "Bariloche, and either way of writing the date".
/// </summary>
public sealed record Need(string Fact, IReadOnlyList<IReadOnlyList<string>> Mentions);

/// <summary>One thing the person said, in one session, at one moment.</summary>
public sealed record Turn(string Session, DateTimeOffset At, string Text);
