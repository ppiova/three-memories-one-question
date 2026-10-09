namespace ThreeMemories.Memories;

/// <summary>
/// The notes file: every statement is appended as a line, and the prompt gets the file from the top
/// until it runs out of room. No infrastructure, readable by a person, and blind below the cut.
/// </summary>
public sealed class FileMemory(string path) : LineMemory
{
    public override string Name => "file";

    public override string How => "a notes file, loaded from the top until the budget runs out";

    public string Path { get; } = path;

    public override async Task RememberAsync(Turn turn, CancellationToken cancellationToken = default)
    {
        await base.RememberAsync(turn, cancellationToken);
        await File.AppendAllTextAsync(Path, Lines[^1] + Environment.NewLine, cancellationToken);
    }

    protected override Task<IReadOnlyList<string>> RankAsync(string question, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Lines);   // the order it was written in; the question changes nothing
}
