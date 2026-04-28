using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudScout.Core.Inference;

/// <summary>
/// <see cref="ILlmInference"/> implementation that calls an OpenAI-compatible
/// <c>/v1/chat/completions</c> HTTP endpoint. Works with llama-server (llama.cpp), Ollama,
/// LM Studio, vLLM, or any compatible API — decoupling model support from NuGet release cycles.
///
/// Supports text-only and text+image inputs. For images, the server must have a multimodal
/// projector loaded (llama-server's <c>--mmproj</c> flag) — without it, the image part is
/// silently ignored by the server and only the text prompt influences the output.
///
/// When <see cref="LlmOptions.AutoLaunch"/> is true, the server is started automatically
/// as a child process on first use via <see cref="LlmServerManager"/>. The user just runs
/// <c>cloudscout scan</c> — no separate terminal needed.
/// </summary>
public sealed class HttpLlmInference : ILlmInference, IDisposable
{
    private readonly LlmOptions _options;
    private readonly LlmServerManager _serverManager;
    private readonly HttpClient _http;
    private readonly ILogger<HttpLlmInference> _logger;

    public HttpLlmInference(
        IOptions<LlmOptions> options,
        LlmServerManager serverManager,
        ILogger<HttpLlmInference> logger)
    {
        _options = options.Value;
        _serverManager = serverManager;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ServerUrl);

    public async Task<string> GenerateAsync(
        string prompt,
        byte[]? imageBytes = null,
        string? imageMimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("LLM server URL is not configured. Set Llm:ServerUrl in appsettings.");

        // Ensure the server is running (auto-launches on first call if configured).
        await _serverManager.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);

        var baseUrl = _options.ServerUrl.TrimEnd('/');
        var requestBody = new ChatCompletionRequest
        {
            Messages = new[] { BuildMessage(prompt, imageBytes, imageMimeType) },
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxGenerationTokens,
            Stream = false,
        };

        _logger.LogDebug("Sending Tier 3 prompt to {Url} ({Length} chars, image={HasImage})",
            baseUrl, prompt.Length, imageBytes is not null);

        var response = await _http.PostAsJsonAsync(
            $"{baseUrl}/v1/chat/completions",
            requestBody,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"LLM server returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
            JsonOptions, cancellationToken).ConfigureAwait(false);

        var content = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
        _logger.LogDebug("Tier 3 response ({Length} chars): {Content}",
            content.Length, content.Length > 200 ? content[..200] : content);
        return content;
    }

    /// <summary>
    /// Builds the user message. Without an image, content is a plain string (most efficient
    /// wire format and what every OpenAI-compatible server understands). With an image, content
    /// becomes a multi-part array per the OpenAI vision spec — text first, then image_url with
    /// a base64 data URI.
    /// </summary>
    private static ChatMessage BuildMessage(string prompt, byte[]? imageBytes, string? imageMimeType)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return new ChatMessage { Role = "user", Content = prompt };
        }

        var mime = string.IsNullOrWhiteSpace(imageMimeType) ? "image/jpeg" : imageMimeType;
        var dataUri = $"data:{mime};base64,{Convert.ToBase64String(imageBytes)}";

        return new ChatMessage
        {
            Role = "user",
            Content = new ContentPart[]
            {
                new() { Type = "text", Text = prompt },
                new() { Type = "image_url", ImageUrl = new ImageUrlPart { Url = dataUri } },
            },
        };
    }

    public void Dispose() => _http.Dispose();

    // ---- OpenAI-compatible wire-format DTOs ------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ChatCompletionRequest
    {
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        public float Temperature { get; set; }
        public int MaxTokens { get; set; }
        public bool Stream { get; set; }
    }

    /// <summary>
    /// Content is typed as <c>object</c> to allow polymorphic serialization: a string for
    /// text-only requests, a <c>ContentPart[]</c> for multimodal requests. System.Text.Json
    /// dispatches on runtime type, so both forms produce valid OpenAI wire format.
    /// </summary>
    private sealed class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public object Content { get; set; } = string.Empty;
    }

    private sealed class ContentPart
    {
        public string Type { get; set; } = string.Empty;
        public string? Text { get; set; }
        public ImageUrlPart? ImageUrl { get; set; }
    }

    private sealed class ImageUrlPart
    {
        public string Url { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ResponseMessage? Message { get; set; }
    }

    /// <summary>
    /// Distinct from <see cref="ChatMessage"/> because response content is always a string —
    /// the server returns plain text, never a content-parts array — so we can deserialize
    /// directly without polymorphic handling.
    /// </summary>
    private sealed class ResponseMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
