namespace CloudScout.Core.Inference;

/// <summary>
/// Configuration for the local LLM inference engine. Bound from the <c>Llm</c> section
/// in appsettings.json / appsettings.Local.json. All paths are relative to the CLI's
/// ContentRoot (AppContext.BaseDirectory) unless absolute.
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// Path to the GGUF model file, e.g. <c>../../models/gemma-4-e2b-it-q8_0.gguf</c>.
    /// Resolved relative to ContentRoot at runtime. Empty string = Tier 3 is disabled
    /// (the orchestrator gracefully skips it).
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Context window size in tokens. Larger = more file text can be included in the prompt
    /// but uses more RAM. 4096 is adequate for first-few-pages classification.
    /// </summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>
    /// Number of model layers to offload to GPU. 0 = pure CPU inference (default).
    /// Set to -1 to offload all layers if a CUDA/Metal GPU is available.
    /// </summary>
    public int GpuLayerCount { get; set; } = 0;

    /// <summary>
    /// Maximum tokens the model may generate per classification response.
    /// Classification responses are short (JSON ~50 tokens); 128 gives headroom.
    /// </summary>
    public int MaxGenerationTokens { get; set; } = 128;

    /// <summary>
    /// Temperature for sampling. 0 = greedy/deterministic, recommended for classification
    /// where we want consistent results on repeated runs.
    /// </summary>
    public float Temperature { get; set; } = 0f;
}
