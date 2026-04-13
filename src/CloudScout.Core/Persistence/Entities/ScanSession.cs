namespace CloudScout.Core.Persistence.Entities;

/// <summary>
/// A single scan operation: enumerate files from a <see cref="CloudConnection"/>
/// and run them through the classification pipeline. Resumable on interruption
/// via <see cref="LastProcessedExternalPath"/>.
/// </summary>
public class ScanSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConnectionId { get; set; }
    public CloudConnection Connection { get; set; } = null!;

    /// <summary>Name of the taxonomy used for classification (e.g. "generic-default").</summary>
    public string TaxonomyName { get; set; } = string.Empty;

    /// <summary>"Running", "Completed", "Failed", or "Cancelled".</summary>
    public string Status { get; set; } = ScanStatus.Running;

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Total files discovered by the crawler (updated as enumeration proceeds).</summary>
    public int TotalFilesFound { get; set; }

    /// <summary>Count of files classified by the pipeline so far.</summary>
    public int ClassifiedCount { get; set; }

    /// <summary>
    /// The last external path the scan successfully processed. Used to resume an interrupted
    /// scan without re-processing files the crawler already enumerated.
    /// </summary>
    public string? LastProcessedExternalPath { get; set; }

    public string? ErrorMessage { get; set; }

    // Navigation
    public ICollection<CrawledFile> Files { get; set; } = new List<CrawledFile>();
}

public static class ScanStatus
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}
