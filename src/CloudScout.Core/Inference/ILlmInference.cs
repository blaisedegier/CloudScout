namespace CloudScout.Core.Inference;

/// <summary>
/// Abstraction over the local LLM engine. Phase C ships with a LlamaSharp implementation;
/// the interface exists so tests can mock inference without loading a multi-GB model file.
/// </summary>
public interface ILlmInference
{
    /// <summary>Returns true if a model is configured and available for inference.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Send a text prompt to the model and return the generated completion. The model is
    /// loaded lazily on the first call and held in memory for subsequent calls.
    /// </summary>
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}
