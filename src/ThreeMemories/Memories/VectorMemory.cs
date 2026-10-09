using Microsoft.Extensions.AI;

namespace ThreeMemories.Memories;

/// <summary>
/// The vector store: every statement becomes an embedding, and the prompt gets the statements whose
/// meaning is closest to the question. Finds a paraphrase among millions; cannot find what is
/// related to the question without resembling it.
/// </summary>
public sealed class VectorMemory(IEmbeddingGenerator<string, Embedding<float>> embeddings) : LineMemory
{
    private readonly List<ReadOnlyMemory<float>> _vectors = [];

    public override string Name => "vector";

    public override string How => "embeddings, most similar to the question first";

    public override async Task RememberAsync(Turn turn, CancellationToken cancellationToken = default)
    {
        await base.RememberAsync(turn, cancellationToken);

        // What is embedded is what was said, not the day it was said on: the date is kept beside it.
        var embedded = await embeddings.GenerateAsync([turn.Text], cancellationToken: cancellationToken);
        _vectors.Add(embedded[0].Vector);
    }

    protected override async Task<IReadOnlyList<string>> RankAsync(string question, CancellationToken cancellationToken)
    {
        var asked = (await embeddings.GenerateAsync([question], cancellationToken: cancellationToken))[0].Vector;

        return Lines
            .Select((line, index) => (line, score: Cosine(asked.Span, _vectors[index].Span)))
            .OrderByDescending(scored => scored.score)
            .Select(scored => scored.line)
            .ToList();
    }

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, lengthA = 0, lengthB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            lengthA += a[i] * a[i];
            lengthB += b[i] * b[i];
        }

        return dot / (Math.Sqrt(lengthA) * Math.Sqrt(lengthB));
    }
}
