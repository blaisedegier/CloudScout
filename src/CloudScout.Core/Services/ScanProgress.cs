namespace CloudScout.Core.Services;

/// <summary>
/// Snapshot of a running scan, emitted periodically so the UI or CLI can render live progress.
/// Intentionally immutable so it's safe to pass through any async boundary.
/// </summary>
public sealed record ScanProgress(
    Guid SessionId,
    int FilesDiscovered,
    int FilesClassified,
    string? CurrentPath,
    string Phase);

public static class ScanPhase
{
    public const string Crawling = "Crawling";
    public const string Classifying = "Classifying";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
