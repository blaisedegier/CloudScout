using Microsoft.Extensions.Logging;

namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Dispatcher that picks the first registered <see cref="ITextExtractor"/> whose
/// <see cref="ITextExtractor.CanHandle"/> returns true and delegates extraction to it.
/// Returns null when no extractor matches — Tier 1 treats that as "no content signal"
/// and relies solely on whatever metadata signal Tier 0 produced.
/// </summary>
public sealed class TextExtractionService
{
    /// <summary>
    /// Default per-file cap in characters. Chosen to be long enough to see the first few pages
    /// of a typical PDF (where category-identifying vocabulary almost always lives) without
    /// holding megabytes in memory per file on large scans.
    /// </summary>
    public const int DefaultMaxChars = 10_000;

    private readonly IReadOnlyList<ITextExtractor> _extractors;
    private readonly ILogger<TextExtractionService> _logger;

    public TextExtractionService(IEnumerable<ITextExtractor> extractors, ILogger<TextExtractionService> logger)
    {
        _extractors = extractors.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Extract text from <paramref name="content"/>, or null if no registered extractor
    /// matches the file type. The caller owns and disposes the stream.
    /// </summary>
    public async Task<string?> ExtractAsync(
        Stream content,
        string? mimeType,
        string fileName,
        int maxChars = DefaultMaxChars,
        CancellationToken cancellationToken = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(mimeType, fileName));
        if (extractor is null)
        {
            _logger.LogDebug("No text extractor registered for mime={Mime} file={File}", mimeType, fileName);
            return null;
        }

        try
        {
            return await extractor.ExtractAsync(content, maxChars, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Corrupt or password-protected documents are common enough in real scans that
            // throwing here would kill the whole scan. Log and return null instead so the
            // pipeline falls back to metadata-only classification for this file.
            _logger.LogWarning(ex, "Text extraction failed for {File} ({ExtractorType}) — skipping content signal",
                fileName, extractor.GetType().Name);
            return null;
        }
    }
}
