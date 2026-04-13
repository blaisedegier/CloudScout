namespace CloudScout.Core.Persistence.Entities;

/// <summary>
/// Metadata for a single file discovered in the user's cloud storage during a scan.
/// Classification results are stored in child <see cref="FileSuggestion"/> rows.
/// </summary>
public class CrawledFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public ScanSession Session { get; set; } = null!;

    /// <summary>The provider's opaque file identifier (e.g. Microsoft Graph DriveItem ID).</summary>
    public string ExternalFileId { get; set; } = string.Empty;

    /// <summary>Full path from the drive root, e.g. "/Documents/Legal/Will 2024.pdf".</summary>
    public string ExternalPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>Parent folder path. Useful for folder-keyword heuristics in Tier 0.</summary>
    public string ParentFolderPath { get; set; } = string.Empty;

    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }

    public DateTime? CreatedUtc { get; set; }
    public DateTime? ModifiedUtc { get; set; }

    public DateTime DiscoveredUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<FileSuggestion> Suggestions { get; set; } = new List<FileSuggestion>();
}
