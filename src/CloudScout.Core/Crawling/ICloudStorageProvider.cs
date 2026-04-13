namespace CloudScout.Core.Crawling;

/// <summary>
/// Provider-agnostic abstraction over a cloud storage service. V1 ships with a
/// OneDrive implementation; Google Drive is planned. All methods are async and
/// respect cancellation so long-running scans can be aborted cleanly.
/// </summary>
public interface ICloudStorageProvider
{
    /// <summary>Stable identifier for this provider (e.g. "OneDrive"). Must match the <c>Provider</c> column on <see cref="Persistence.Entities.CloudConnection"/>.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Launch an interactive authentication flow, prompting the user to sign in and consent.
    /// The resulting token material is persisted into the provider's encrypted token cache;
    /// this method returns the identifiers needed to look the account up later.
    /// </summary>
    /// <param name="deviceCodePrompt">
    /// Optional callback invoked with a user-visible message when the device-code flow needs
    /// the user to visit a URL and enter a code. CLI implementations typically write this to stdout;
    /// GUI implementations might show a dialog.
    /// </param>
    Task<ConnectionResult> AuthenticateInteractiveAsync(
        Func<string, Task>? deviceCodePrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recursively enumerate every file visible to the authenticated account. Folders are
    /// descended into breadth-first so early progress is visible. Throttling from the upstream
    /// API is handled with exponential backoff; consumers receive metadata only — content is
    /// not downloaded here.
    /// </summary>
    /// <param name="homeAccountId">The <see cref="ConnectionResult.HomeAccountId"/> returned from a previous successful authentication.</param>
    /// <param name="resumeFromPath">
    /// If non-null, enumeration will skip files whose <see cref="RemoteFileMetadata.ExternalPath"/>
    /// comes strictly before this path (in ordinal ordering). Enables resumption of an interrupted scan.
    /// </param>
    IAsyncEnumerable<RemoteFileMetadata> EnumerateFilesAsync(
        string homeAccountId,
        string? resumeFromPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download the content of a single file as a seekable stream. Used by Tier 1 text extraction
    /// and Tier 3 image/content input to the LLM. The caller is responsible for disposing the stream.
    /// </summary>
    Task<Stream> DownloadAsync(
        string homeAccountId,
        string externalFileId,
        CancellationToken cancellationToken = default);
}
