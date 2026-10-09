using AgentMemory.Inference;
using Microsoft.Extensions.AI;

namespace ThreeMemories;

/// <summary>
/// The two models every memory here shares: one that chats (and that the graph memory uses to read
/// what was said), and one that turns text into embeddings.
/// </summary>
public sealed record Models(
    IChatClient Chat,
    IEmbeddingGenerator<string, Embedding<float>> Embeddings,
    InferenceProviderSettings Settings)
{
    /// <summary>
    /// What is used when nothing is configured: the Ollama started by <c>compose.yaml</c>.
    /// 127.0.0.1 rather than localhost, because on Windows localhost tries IPv6 first.
    /// </summary>
    private static readonly Dictionary<string, string> Local = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AI_INFERENCE_PROVIDER"] = "ollama",
        ["OLLAMA_ENDPOINT"] = "http://127.0.0.1:11435/v1",
        ["OLLAMA_MODEL"] = "gemma3:12b",
        ["OLLAMA_EMBEDDING_MODEL"] = "bge-m3",
    };

    /// <summary>
    /// Local models unless the environment names something else. To use a hosted model, set
    /// <c>AI_INFERENCE_PROVIDER</c> (openai, azure, openai-compatible, ...) and that provider's
    /// variables, as documented in AgentMemory's <c>docs/configuration/inference-providers.md</c>.
    /// </summary>
    public static Models FromEnvironment()
    {
        var resolution = InferenceProviderEnvironment.Resolve(Read);
        if (resolution.Settings is not { } settings)
        {
            throw new InvalidOperationException(
                $"No model is configured. {resolution.Diagnostic} {resolution.EmbeddingDiagnostic}");
        }

        if (!InferenceClientFactory.TryCreateChatClient(settings, "three-memories", out var chat, out var whyNoChat))
        {
            throw new InvalidOperationException($"Could not create the chat model. {whyNoChat}");
        }

        if (!InferenceClientFactory.TryCreateEmbeddingGenerator(settings, out var embeddings, out var whyNoEmbeddings))
        {
            throw new InvalidOperationException($"Could not create the embedding model. {whyNoEmbeddings}");
        }

        return new Models(chat, embeddings, settings);
    }

    private static string? Read(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } set ? set : Local.GetValueOrDefault(variable);
}
