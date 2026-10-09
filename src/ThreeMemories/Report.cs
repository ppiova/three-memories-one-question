using System.Globalization;
using System.Text;
using ThreeMemories.Memories;

namespace ThreeMemories;

/// <summary>Writes the run as Markdown: the scoreboard, then every prompt and every answer behind it.</summary>
internal static class Report
{
    public static string Markdown(
        Scenario scenario,
        Models models,
        IReadOnlyList<Written> written,
        IReadOnlyList<Asked> asked,
        GraphSnapshot? graph,
        string? graphOwner)
    {
        var md = new StringBuilder();
        var statements = scenario.Sessions.Sum(session => session.Messages.Count);
        var everything = string.Join('\n', scenario.Turns().Select(turn => $"- [{turn.At:yyyy-MM-dd}] {turn.Text}"));

        md.AppendLine("# Three memories, one question");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"- **Scenario:** {scenario.Title} ({statements} statements, {scenario.Sessions.Count} sessions, ≈{Text.Tokens(everything)} tokens in all)");
        md.AppendLine(CultureInfo.InvariantCulture, $"- **Asked on:** {scenario.AskedOn:dddd, MMMM d, yyyy}, in a new conversation");
        md.AppendLine(CultureInfo.InvariantCulture, $"- **Models:** {models.Settings.Summary}");
        md.AppendLine();

        md.AppendLine("## Result");
        md.AppendLine();
        md.AppendLine($"| Memory | {string.Join(" | ", asked.Select(a => a.Question.Kind))} |");
        md.AppendLine($"|---|{string.Concat(asked.Select(_ => ":-:|"))}");
        foreach (var memory in written)
        {
            var row = asked.Select(a => a.Attempts.Single(attempt => attempt.Memory == memory.Memory).Verdict);
            md.AppendLine($"| **{memory.Memory}** | {string.Join(" | ", row)} |");
        }

        md.AppendLine();
        md.AppendLine("✓ the facts reached the prompt and the answer is right · ✗ a fact never reached the prompt, and nothing said so · ~ the facts were in the prompt and the answer still missed the point");
        md.AppendLine();
        md.AppendLine("Whether a fact reached the prompt is checked by looking for it in the text. Whether an answer is right is graded by the same model with a yes/no question, so read the answers below before trusting a ✓ or a ~.");
        md.AppendLine();

        md.AppendLine("## How each memory works, and what writing cost");
        md.AppendLine();
        md.AppendLine("| Memory | How it chooses what goes in the prompt | Time to hear everything |");
        md.AppendLine("|---|---|--:|");
        foreach (var memory in written)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| **{memory.Memory}** | {memory.How} | {memory.Took.TotalSeconds:0.0} s |");
        }

        md.AppendLine();

        foreach (var a in asked)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"## {a.Question.Kind}");
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"> {a.Question.Text}");
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"The prompt has to contain: {string.Join("; ", a.Question.Needs.Select(need => need.Fact))}. A right answer says: *{a.Question.RightAnswer}* Room in the prompt: **{a.Budget} tokens** for each memory.");
            md.AppendLine();
            md.AppendLine("| Memory | | Tokens used | Missing from the prompt | Smallest prompt that would have worked |");
            md.AppendLine("|---|:-:|--:|---|--:|");
            foreach (var attempt in a.Attempts)
            {
                var missing = attempt.MissingFromPrompt.Count == 0 ? "nothing" : string.Join("; ", attempt.MissingFromPrompt.Select(need => need.Fact));
                var smallest = attempt.Memory == "graph"
                    ? "n/a"
                    : attempt.SmallestBudgetThatWorks is { } tokens ? $"{tokens} tokens" : "none";
                md.AppendLine(CultureInfo.InvariantCulture, $"| **{attempt.Memory}** | {attempt.Verdict} | {attempt.PromptTokens} | {missing} | {smallest} |");
            }

            md.AppendLine();
            foreach (var attempt in a.Attempts)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"**{attempt.Memory}** answered:");
                md.AppendLine();
                md.AppendLine(Quote(attempt.Answer));
                md.AppendLine();
                md.AppendLine("<details><summary>the prompt it was given</summary>");
                md.AppendLine();
                md.AppendLine("```text");
                md.AppendLine(string.IsNullOrWhiteSpace(attempt.Prompt) ? "(nothing)" : attempt.Prompt);
                md.AppendLine("```");
                md.AppendLine();
                md.AppendLine("</details>");
                md.AppendLine();
            }
        }

        if (graph is not null)
        {
            md.AppendLine("## What the graph memory wrote");
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"{graph.Entities.Count} entities, {graph.Facts.Count} facts, {graph.Relationships.Count} relationships and {graph.Preferences.Count} preferences, all extracted by the model from the statements. Owner id of this run: `{graphOwner}`.");
            md.AppendLine();

            if (graph.Relationships.Count > 0)
            {
                md.AppendLine("```mermaid");
                md.AppendLine("graph LR");
                var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string Id(string name)
                {
                    if (!ids.TryGetValue(name, out var id))
                    {
                        id = $"n{ids.Count}";
                        ids[name] = id;
                        md.AppendLine(CultureInfo.InvariantCulture, $"  {id}[\"{Label(name)}\"]");
                    }

                    return id;
                }

                foreach (var relationship in graph.Relationships)
                {
                    var source = Id(relationship.Source);
                    var target = Id(relationship.Target);
                    md.AppendLine(CultureInfo.InvariantCulture, $"  {source} -->|{Label(relationship.Relation)}| {target}");
                }

                md.AppendLine("```");
                md.AppendLine();
            }

            md.AppendLine("<details><summary>facts</summary>");
            md.AppendLine();
            md.AppendLine("| Subject | Predicate | Object | When |");
            md.AppendLine("|---|---|---|---|");
            foreach (var fact in graph.Facts)
            {
                var when = fact.Until.Length > 0 ? $"{Day(fact.Since)} → {Day(fact.Until)}" : Day(fact.Since);
                md.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(fact.Subject)} | {Cell(fact.Predicate)} | {Cell(fact.Object)} | {when} |");
            }

            md.AppendLine();
            md.AppendLine("</details>");
            md.AppendLine();
            md.AppendLine("<details><summary>entities and preferences</summary>");
            md.AppendLine();
            foreach (var group in graph.Entities.GroupBy(entity => entity.Type))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- **{(group.Key.Length == 0 ? "untyped" : group.Key)}:** {string.Join(", ", group.Select(entity => entity.Name))}");
            }

            foreach (var preference in graph.Preferences)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- *preference ({preference.Category}):* {preference.Preference}");
            }

            md.AppendLine();
            md.AppendLine("</details>");
        }

        return md.ToString();
    }

    private static string Quote(string text) =>
        string.Join('\n', text.Split('\n').Select(line => $"> {line.TrimEnd()}"));

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", " ");

    private static string Label(string text) => text.Replace("\"", "'").Replace("|", "/");

    private static string Day(string timestamp) => timestamp.Length >= 10 ? timestamp[..10] : timestamp;
}
