namespace CloudScout.Core.Persistence.Entities;

/// <summary>
/// A single classification attempt for a <see cref="CrawledFile"/>. One file may have
/// multiple suggestions (one per tier that produced a candidate result). The UI picks
/// the highest-confidence suggestion to present; lower-ranked ones are retained for audit.
/// </summary>
public class FileSuggestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FileId { get; set; }
    public CrawledFile File { get; set; } = null!;

    /// <summary>Category ID from the active taxonomy, e.g. "financial.banking".</summary>
    public string SuggestedCategoryId { get; set; } = string.Empty;

    /// <summary>Confidence score in [0.0, 1.0]. Higher = more certain.</summary>
    public double ConfidenceScore { get; set; }

    /// <summary>Which tier produced this suggestion: 0 = metadata, 1 = keyword, 3 = LLM.</summary>
    public int ClassificationTier { get; set; }

    /// <summary>
    /// Human-readable explanation, e.g. "filename contains 'will'" or "LLM matched on extracted text".
    /// Shown to the user alongside the suggestion to build trust in the system.
    /// </summary>
    public string ClassificationReason { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>"Pending", "Accepted", or "Rejected" — tracks the user's review decision.</summary>
    public string UserStatus { get; set; } = SuggestionStatus.Pending;
}

public static class SuggestionStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
}
