namespace CloudScout.Core.Classification.Extraction;

/// <summary>
/// Extracts plain text from a single document type. Implementations are stateless and safe
/// to register as singletons. The dispatcher (<see cref="TextExtractionService"/>) walks the
/// registered extractors in order, asking each whether it can handle the file and delegating
/// to the first match.
/// </summary>
public interface ITextExtractor
{
    /// <summary>Returns true if this extractor can handle the given MIME type or filename.</summary>
    bool CanHandle(string? mimeType, string fileName);

    /// <summary>
    /// Read text from <paramref name="content"/>. The caller owns and disposes the stream.
    /// <paramref name="maxChars"/> caps the output — classification only needs the first few
    /// pages, and holding megabytes of text per file in memory across a scan is wasteful.
    /// </summary>
    Task<string> ExtractAsync(Stream content, int maxChars, CancellationToken cancellationToken = default);
}
