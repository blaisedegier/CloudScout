namespace CloudScout.Core.Inference;

/// <summary>
/// Configuration for the LLM inference endpoint. Bound from the <c>Llm</c> section in
/// appsettings.json / appsettings.Local.json. When <see cref="ServerUrl"/> is empty, Tier 3
/// is gracefully disabled — the orchestrator skips it with zero overhead.
///
/// The server is expected to expose an OpenAI-compatible <c>/v1/chat/completions</c> endpoint.
/// Compatible servers: llama-server (llama.cpp), Ollama, LM Studio, vLLM, any OpenAI proxy.
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// Base URL of the inference server, e.g. <c>http://localhost:8080</c>. Must expose an
    /// OpenAI-compatible <c>/v1/chat/completions</c> endpoint. Empty = Tier 3 disabled.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

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
