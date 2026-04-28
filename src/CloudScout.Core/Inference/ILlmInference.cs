namespace CloudScout.Core.Inference;

/// <summary>
/// Abstraction over the local LLM engine. Implementations target an OpenAI-compatible
/// chat-completions endpoint (llama-server, Ollama, LM Studio, etc.). The interface exists
/// so tests can mock inference without loading a multi-GB model file.
/// </summary>
public interface ILlmInference
{
    /// <summary>Returns true if a model endpoint is configured and reachable.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Send a prompt — and optionally a single image — to the model and return the generated
    /// completion. When <paramref name="imageBytes"/> is non-null, the request is built as a
    /// multi-part message containing both a text part and an <c>image_url</c> part with a base64
    /// data URI. This requires the underlying server to have a multimodal projector loaded
    /// (llama-server's <c>--mmproj</c> flag); without it, image input is silently ignored by
    /// the server, which classifies on text alone.
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        byte[]? imageBytes = null,
        string? imageMimeType = null,
        CancellationToken cancellationToken = default);
}
