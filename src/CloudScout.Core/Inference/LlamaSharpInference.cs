using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace CloudScout.Core.Inference;

/// <summary>
/// <see cref="ILlmInference"/> backed by LlamaSharp (llama.cpp). The model file is loaded
/// lazily on first <see cref="GenerateAsync"/> call — startup is fast, and if no file is
/// classified below the Tier 0+1 threshold, the model is never loaded at all.
///
/// Thread-safe: inference calls are serialised through a semaphore so the underlying
/// llama.cpp context (which is not thread-safe) isn't accessed concurrently.
/// </summary>
public sealed class LlamaSharpInference : ILlmInference, IAsyncDisposable
{
    private readonly LlmOptions _options;
    private readonly ILogger<LlamaSharpInference> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LLamaWeights? _model;
    private LLamaContext? _context;
    private bool _loadAttempted;

    public LlamaSharpInference(IOptions<LlmOptions> options, ILogger<LlamaSharpInference> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.ModelPath)) return false;
            var resolved = ResolvePath(_options.ModelPath);
            return File.Exists(resolved);
        }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureModelLoaded();
            if (_context is null)
                throw new InvalidOperationException("LLM model is not loaded. Check the Llm:ModelPath configuration.");

            var executor = new StatelessExecutor(_model!, _context.Params);

            // Greedy sampling = deterministic output. For classification we want consistent
            // results on repeated runs, not creative diversity. If the user configured a
            // non-zero temperature, use DefaultSamplingPipeline instead.
            ISamplingPipeline samplingPipeline = _options.Temperature <= 0f
                ? new GreedySamplingPipeline()
                : new DefaultSamplingPipeline { Temperature = _options.Temperature };

            var inferenceParams = new InferenceParams
            {
                MaxTokens = _options.MaxGenerationTokens,
                SamplingPipeline = samplingPipeline,
                AntiPrompts = new[] { "\n\n", "```" }, // stop after first complete JSON block
            };

            var sb = new StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken).ConfigureAwait(false))
            {
                sb.Append(token);
            }

            return sb.ToString().Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureModelLoaded()
    {
        if (_model is not null) return;
        if (_loadAttempted) return; // don't retry a failed load on every file
        _loadAttempted = true;

        var path = ResolvePath(_options.ModelPath);
        if (!File.Exists(path))
        {
            _logger.LogWarning("LLM model file not found at {Path}. Tier 3 classification is disabled.", path);
            return;
        }

        _logger.LogInformation("Loading LLM model from {Path} (contextSize={ContextSize}, gpuLayers={GpuLayers})...",
            path, _options.ContextSize, _options.GpuLayerCount);

        var modelParams = new ModelParams(path)
        {
            ContextSize = (uint)_options.ContextSize,
            GpuLayerCount = _options.GpuLayerCount,
        };

        _model = LLamaWeights.LoadFromFile(modelParams);
        _context = _model.CreateContext(modelParams);

        _logger.LogInformation("LLM model loaded successfully.");
    }

    private static string ResolvePath(string modelPath)
    {
        if (Path.IsPathRooted(modelPath)) return modelPath;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, modelPath));
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _context?.Dispose();
            _model?.Dispose();
            _context = null;
            _model = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
