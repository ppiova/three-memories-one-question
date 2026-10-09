using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;
using ThreeMemories.Memories;

namespace ThreeMemories;

/// <summary>
/// The experiment: every memory hears the same statements, then gets the same question and the
/// same room in the prompt. What differs is only how each one decides what to bring back.
/// </summary>
public sealed class Experiment(Scenario scenario, Models models, ScenarioClock clock, int fallbackBudget)
{
    public async Task<IReadOnlyList<Written>> WriteAsync(IReadOnlyList<IMemory> memories, Action<string> progress)
    {
        var took = memories.ToDictionary(memory => memory.Name, _ => TimeSpan.Zero);
        var turns = scenario.Turns().ToList();

        for (var i = 0; i < turns.Count; i++)
        {
            clock.UtcNow = turns[i].At;
            foreach (var memory in memories)
            {
                var watch = Stopwatch.StartNew();
                await memory.RememberAsync(turns[i]);
                took[memory.Name] += watch.Elapsed;
            }

            progress($"{i + 1}/{turns.Count}  {turns[i].At:yyyy-MM-dd}  {Shorten(turns[i].Text, 64)}");
        }

        return memories.Select(memory => new Written(memory.Name, memory.How, took[memory.Name])).ToList();
    }

    public async Task<Asked> AskAsync(Question question, IReadOnlyList<IMemory> memories)
    {
        clock.UtcNow = scenario.AskedAt;

        // The graph memory decides how much it brings back. The other two then get exactly that much
        // room, so nobody wins by being given a bigger prompt.
        var prompts = new Dictionary<string, string>();
        if (memories.FirstOrDefault(memory => memory is GraphMemory) is { } graph)
        {
            prompts[graph.Name] = await graph.RecallAsync(question.Text, budgetTokens: null);
        }

        var graphTokens = prompts.Values.Select(Text.Tokens).FirstOrDefault();
        var budget = graphTokens > 0 ? graphTokens : fallbackBudget;

        var attempts = new List<Attempt>();
        foreach (var memory in memories)
        {
            if (!prompts.TryGetValue(memory.Name, out var prompt))
            {
                prompt = await memory.RecallAsync(question.Text, budget);
            }

            var answer = await AnswerAsync(prompt, question.Text);
            var smallestBudget = memory is LineMemory lines ? await lines.SmallestBudgetThatWorksAsync(question) : null;

            attempts.Add(new Attempt(
                memory.Name,
                prompt,
                Text.Tokens(prompt),
                Text.Missing(question, prompt),
                answer,
                await SaysAsync(answer, question.RightAnswer),
                smallestBudget));
        }

        return new Asked(question, budget, attempts);
    }

    /// <summary>The same model and the same instructions for every memory; only the MEMORY block changes.</summary>
    private async Task<string> AnswerAsync(string memory, string question)
    {
        var today = scenario.AskedOn.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        var instructions = $"""
            {scenario.AssistantBrief}
            Today is {today}.
            Answer only from the MEMORY below. If two commitments in it fall on the same dates in
            different places, say so first; otherwise do not mention conflicts.
            Keep the answer under 90 words.

            MEMORY
            {(string.IsNullOrWhiteSpace(memory) ? "(nothing)" : memory)}
            """;

        var response = await models.Chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, question)],
            new ChatOptions { Temperature = 0 });

        return response.Text.Trim();
    }

    /// <summary>
    /// Grades an answer against what a right answer says. The grader is the same model, asked a
    /// yes/no question, so it can be wrong: the report prints every answer for that reason.
    /// </summary>
    private async Task<bool> SaysAsync(string answer, string claim)
    {
        var response = await models.Chat.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "You grade answers. Reply with one word: YES or NO."),
                new ChatMessage(ChatRole.User, $"CLAIM: {claim}\n\nANSWER: {answer}\n\nDoes the ANSWER clearly state the CLAIM?"),
            ],
            new ChatOptions { Temperature = 0 });

        return response.Text.TrimStart().StartsWith("YES", StringComparison.OrdinalIgnoreCase);
    }

    private static string Shorten(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";
}

public sealed record Written(string Memory, string How, TimeSpan Took);

public sealed record Asked(Question Question, int Budget, IReadOnlyList<Attempt> Attempts);

/// <summary>What one memory put in the prompt for one question, and what the model answered with it.</summary>
public sealed record Attempt(
    string Memory,
    string Prompt,
    int PromptTokens,
    IReadOnlyList<Need> MissingFromPrompt,
    string Answer,
    bool AnswerIsRight,
    int? SmallestBudgetThatWorks)
{
    /// <summary>
    /// ✗ a fact never reached the prompt · ~ the facts were in the prompt and the answer still
    /// missed the point · ✓ the facts reached the prompt and the answer is right.
    /// </summary>
    public string Verdict =>
        MissingFromPrompt.Count > 0 ? "✗"
        : AnswerIsRight ? "✓"
        : "~";
}
