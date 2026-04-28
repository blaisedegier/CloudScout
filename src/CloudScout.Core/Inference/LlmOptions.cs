namespace CloudScout.Core.Inference;

/// <summary>
/// Configuration for the LLM inference endpoint. Bound from the <c>Llm</c> section in
/// appsettings.json / appsettings.Local.json. When <see cref="ServerUrl"/> is empty, Tier 3
/// is gracefully disabled — the orchestrator skips it with zero overhead.
///
/// The server is expected to expose an OpenAI-compatible <c>/v1/chat/completions</c> endpoint.
/// Compatible servers: llama-server (llama.cpp), Ollama, LM Studio, vLLM, any OpenAI proxy.
///
/// When <see cref="AutoLaunch"/> is true and the server isn't already running, CloudScout
/// spawns llama-server as a child process using <see cref="ServerExePath"/> and
/// <see cref="ModelPath"/>, waits for readiness, and kills it on exit.
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
    /// When true, CloudScout will attempt to start llama-server automatically if the
    /// <see cref="ServerUrl"/> endpoint isn't reachable. Requires <see cref="ServerExePath"/>
    /// and <see cref="ModelPath"/> to be configured.
    /// </summary>
    public bool AutoLaunch { get; set; } = true;

    /// <summary>
    /// Path to the llama-server executable. Resolved in order: absolute path, then PATH lookup.
    /// Examples: <c>C:\Tools\llama-cpp\llama-server.exe</c>, or just <c>llama-server</c> if on PATH.
    /// </summary>
    public string ServerExePath { get; set; } = "llama-server";

    /// <summary>
    /// Path to the GGUF model file for auto-launch. Resolved relative to the working directory.
    /// Only used when <see cref="AutoLaunch"/> is true.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to a multimodal projector (mmproj) GGUF for vision input. When set and
    /// the file exists, llama-server is launched with <c>--mmproj &lt;path&gt;</c> so it can
    /// process image_url content parts. Empty = vision disabled (image inputs ignored by the
    /// server, but the rest of Tier 3 still works on metadata + extracted text).
    /// </summary>
    public string MmprojPath { get; set; } = string.Empty;

    /// <summary>Context window size passed to llama-server via <c>-c</c>.</summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>
    /// Maximum tokens the model may generate per classification response.
    /// 2048 gives Gemma 4's thinking mode room to reason before outputting JSON.
    /// </summary>
    public int MaxGenerationTokens { get; set; } = 2048;

    /// <summary>
    /// Temperature for sampling. 0 = greedy/deterministic, recommended for classification
    /// where we want consistent results on repeated runs.
    /// </summary>
    public float Temperature { get; set; } = 0f;
}
