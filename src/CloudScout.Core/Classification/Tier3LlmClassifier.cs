using CloudScout.Core.Inference;
using CloudScout.Core.Taxonomy;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CloudScout.Core.Classification;

/// <summary>
/// The most expensive tier — sends file metadata + extracted text to a local LLM (Gemma 4 E2B
/// via LlamaSharp) and parses the structured JSON response into a <see cref="ClassificationResult"/>.
/// Only invoked for files where Tier 0 and Tier 1 couldn't reach the confidence threshold, and only
/// when a model file is actually configured.
///
/// Designed to fail gracefully: if the model isn't available, returns empty. If the model's
/// output isn't valid JSON, logs a warning and returns empty. Neither case should abort a scan.
/// </summary>
public sealed class Tier3LlmClassifier : IClassificationTier
{
    private readonly ILlmInference _inference;
    private readonly ILogger<Tier3LlmClassifier> _logger;

    public Tier3LlmClassifier(ILlmInference inference, ILogger<Tier3LlmClassifier> logger)
    {
        _inference = inference;
        _logger = logger;
    }

    public int Tier => 3;

    public async Task<IReadOnlyList<ClassificationResult>> ClassifyAsync(
        ClassificationContext context,
        TaxonomyDefinition taxonomy,
        CancellationToken cancellationToken = default)
    {
        if (!_inference.IsAvailable)
        {
            _logger.LogDebug("Tier 3 skipped — no LLM model configured.");
            return Array.Empty<ClassificationResult>();
        }

        var prompt = GemmaPromptBuilder.BuildClassificationPrompt(
            context.FileName,
            context.ParentFolderPath,
            context.MimeType,
            context.ExtractedText,
            taxonomy);

        string raw;
        try
        {
            raw = await _inference.GenerateAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tier 3 inference failed for {File}", context.FileName);
            return Array.Empty<ClassificationResult>();
        }

        return ParseResponse(raw, context.FileName, taxonomy);
    }

    private IReadOnlyList<ClassificationResult> ParseResponse(
        string raw,
        string fileName,
        TaxonomyDefinition taxonomy)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogDebug("Tier 3 returned empty response for {File}", fileName);
            return Array.Empty<ClassificationResult>();
        }

        // Gemma 4 has a "thinking" mode that emits reasoning tokens before the actual
        // answer. The response may look like:
        //   <|think|>The file appears to be...<|/think|>{"categoryId": "...", ...}
        // Strip everything up to and including the closing think tag, then extract the
        // first JSON object. Also handles markdown code fences.
        var json = StripThinkingAndExtractJson(raw);

        LlmClassificationResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<LlmClassificationResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Tier 3 returned unparseable JSON for {File}: {Raw} — {Error}",
                fileName, raw.Length > 200 ? raw[..200] : raw, ex.Message);
            return Array.Empty<ClassificationResult>();
        }

        if (response is null ||
            string.IsNullOrWhiteSpace(response.CategoryId) ||
            string.Equals(response.CategoryId, "unclassified", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ClassificationResult>();
        }

        // Validate that the model returned a category that actually exists in the taxonomy.
        // Hallucinated category ids are discarded rather than persisted.
        if (taxonomy.GetById(response.CategoryId) is null)
        {
            _logger.LogDebug("Tier 3 returned unknown category '{Category}' for {File} — discarding",
                response.CategoryId, fileName);
            return Array.Empty<ClassificationResult>();
        }

        var confidence = Math.Clamp(response.Confidence, 0.0, 1.0);
        var reason = string.IsNullOrWhiteSpace(response.Reason) ? "LLM classification" : response.Reason;

        return new[]
        {
            new ClassificationResult(
                CategoryId: response.CategoryId,
                ConfidenceScore: Math.Round(confidence, 3),
                Tier: Tier,
                Reason: $"LLM: {reason}")
        };
    }

    /// <summary>
    /// Strips Gemma 4's thinking block, markdown fences, and any other wrapper around the
    /// JSON object. Returns just the first <c>{...}</c> found, or the raw input if none found.
    /// </summary>
    private static string StripThinkingAndExtractJson(string raw)
    {
        var text = raw.Trim();

        // Strip <|think|>...<|/think|> or <think>...</think> blocks (Gemma 4 reasoning output)
        var thinkEndPatterns = new[] { "<|/think|>", "</think>", "<turn|>" };
        foreach (var pattern in thinkEndPatterns)
        {
            var idx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                text = text.Substring(idx + pattern.Length).Trim();
                break;
            }
        }

        // Strip markdown code fences
        if (text.StartsWith("```"))
        {
            var nlIdx = text.IndexOf('\n');
            if (nlIdx >= 0) text = text.Substring(nlIdx + 1);
            if (text.EndsWith("```")) text = text[..^3];
            text = text.Trim();
        }

        // Extract the first JSON object if there's surrounding text
        var startIdx = text.IndexOf('{');
        var endIdx = text.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx)
            text = text.Substring(startIdx, endIdx - startIdx + 1);

        return text;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class LlmClassificationResponse
    {
        public string? CategoryId { get; set; }
        public double Confidence { get; set; }
        public string? Reason { get; set; }
    }
}
