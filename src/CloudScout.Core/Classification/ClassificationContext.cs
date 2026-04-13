namespace CloudScout.Core.Classification;

/// <summary>
/// Mutable bag of per-file signals threaded through the classification pipeline. Tier 0 only
/// inspects metadata; Tier 1 populates <see cref="ExtractedText"/> and re-reads metadata; Tier 3
/// may fall back on raw bytes via <see cref="SourceBytes"/>. Kept plain so tests can hand-craft
/// inputs without going through the full crawler/extractor stack.
/// </summary>
public sealed class ClassificationContext
{
    public required string FileName { get; init; }
    public required string ParentFolderPath { get; init; }
    public required string FullPath { get; init; }

    public string? MimeType { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>Text extracted by Tier 1. Null until extraction runs (or if extraction is unsupported for the file type).</summary>
    public string? ExtractedText { get; set; }

    /// <summary>Raw file bytes (for Tier 3 multimodal input). Null unless explicitly loaded — loading every file is expensive.</summary>
    public byte[]? SourceBytes { get; set; }
}
