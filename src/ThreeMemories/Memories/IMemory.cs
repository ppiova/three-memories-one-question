namespace ThreeMemories.Memories;

/// <summary>
/// The whole contract of an agent memory, reduced to its two moments: hear something, and later
/// bring back what matters for a question.
/// </summary>
public interface IMemory : IAsyncDisposable
{
    string Name { get; }

    /// <summary>One line on how this memory chooses what goes into the prompt.</summary>
    string How { get; }

    Task RememberAsync(Turn turn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the text this memory would put in the prompt for <paramref name="question"/>.
    /// </summary>
    /// <param name="budgetTokens">How much room it gets. <c>null</c> lets the memory decide.</param>
    Task<string> RecallAsync(string question, int? budgetTokens, CancellationToken cancellationToken = default);
}
