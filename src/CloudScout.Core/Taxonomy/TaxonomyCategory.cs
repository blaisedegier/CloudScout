namespace CloudScout.Core.Taxonomy;

/// <summary>
/// One classifiable bucket within a taxonomy. Every category carries a collection of match
/// signals (filename, folder, content keywords and phrases) that Tier 0 and Tier 1 evaluate
/// to score files. Categories may optionally be nested under a <see cref="ParentId"/> for
/// display purposes — the scoring logic treats the graph as flat.
/// </summary>
public sealed class TaxonomyCategory
{
    /// <summary>Stable identifier, e.g. <c>financial.banking</c>. Unique within a taxonomy.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in UI, e.g. "Banking".</summary>
    public required string DisplayName { get; init; }

    /// <summary>Optional parent category id for hierarchical display. Null for top-level categories.</summary>
    public string? ParentId { get; init; }

    /// <summary>Case-insensitive substrings matched against file names (e.g. "statement", "invoice").</summary>
    public IReadOnlyList<string> FilenameKeywords { get; init; } = Array.Empty<string>();

    /// <summary>Case-insensitive substrings matched against parent folder paths (e.g. "banking", "legal").</summary>
    public IReadOnlyList<string> FolderKeywords { get; init; } = Array.Empty<string>();

    /// <summary>Case-insensitive substrings matched against extracted document text.</summary>
    public IReadOnlyList<string> ContentKeywords { get; init; } = Array.Empty<string>();

    /// <summary>Case-insensitive multi-word phrases matched against extracted document text (higher signal than single keywords).</summary>
    public IReadOnlyList<string> ContentPhrases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Substrings whose presence in the filename, folder, or content should *prevent* this category
    /// from matching. Useful for disambiguating overlapping vocab (e.g. "bank statement" vs "personal statement").
    /// </summary>
    public IReadOnlyList<string> NegativeKeywords { get; init; } = Array.Empty<string>();

    /// <summary>MIME types that are preferred for this category. Non-matching MIME types dampen the score but don't exclude.</summary>
    public IReadOnlyList<string> MimeTypes { get; init; } = Array.Empty<string>();

    /// <summary>Confidence ceiling applied when this category's rules match, in the range [0.0, 1.0].</summary>
    public double BaseConfidence { get; init; } = 0.7;
}
