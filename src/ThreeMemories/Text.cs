using System.Globalization;

namespace ThreeMemories;

internal static class Text
{
    /// <summary>
    /// A rough token count: four characters per token. Good enough to compare memories with each
    /// other, which is all this demo does with it.
    /// </summary>
    public static int Tokens(string text) => (int)Math.Ceiling(text.Length / 4.0);

    /// <summary>True when <paramref name="text"/> contains <paramref name="phrase"/>, ignoring case and accents.</summary>
    public static bool Mentions(string text, string phrase) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            text, phrase, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>The needed facts that <paramref name="text"/> does not contain.</summary>
    public static IReadOnlyList<Need> Missing(Question question, string text) =>
        question.Needs
            .Where(need => !need.Mentions.All(spellings => spellings.Any(spelling => Mentions(text, spelling))))
            .ToList();
}
