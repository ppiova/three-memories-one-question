namespace ThreeMemories.Memories;

/// <summary>
/// A memory that keeps what it heard as lines and, when asked, puts them in some order and takes
/// lines from the top until the budget runs out. A file and a vector store differ only in the order.
/// </summary>
public abstract class LineMemory : IMemory
{
    protected List<string> Lines { get; } = [];

    public abstract string Name { get; }

    public abstract string How { get; }

    public virtual Task RememberAsync(Turn turn, CancellationToken cancellationToken = default)
    {
        Lines.Add($"- [{turn.At:yyyy-MM-dd}] {turn.Text}");
        return Task.CompletedTask;
    }

    /// <summary>All the lines, best first, for this question.</summary>
    protected abstract Task<IReadOnlyList<string>> RankAsync(string question, CancellationToken cancellationToken);

    public async Task<string> RecallAsync(string question, int? budgetTokens, CancellationToken cancellationToken = default)
    {
        var ranked = await RankAsync(question, cancellationToken);
        return string.Join('\n', Fit(ranked, budgetTokens ?? int.MaxValue));
    }

    /// <summary>
    /// The smallest budget at which every fact the question needs is in the prompt, or <c>null</c>
    /// when the memory never heard one of them.
    /// </summary>
    public async Task<int?> SmallestBudgetThatWorksAsync(Question question, CancellationToken cancellationToken = default)
    {
        var ranked = await RankAsync(question.Text, cancellationToken);
        for (var count = 1; count <= ranked.Count; count++)
        {
            var prompt = string.Join('\n', ranked.Take(count));
            if (Text.Missing(question, prompt).Count == 0) return Text.Tokens(prompt);
        }

        return null;
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IEnumerable<string> Fit(IReadOnlyList<string> ranked, int budgetTokens)
    {
        var used = 0;
        foreach (var line in ranked)
        {
            used += Text.Tokens(line + "\n");
            if (used > budgetTokens) yield break;   // the cut: whatever is below it never reaches the model
            yield return line;
        }
    }
}
