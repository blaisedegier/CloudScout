using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace CloudScout.Core.Crawling.GoogleDrive;

/// <summary>
/// <see cref="ICloudStorageProvider"/> implementation backed by Google Drive v3. Mirrors the
/// OneDrive provider's BFS enumeration shape — Google Drive has no native paths, so we build
/// folder paths as we descend the tree.
/// </summary>
public sealed class GoogleDriveProvider : ICloudStorageProvider
{
    private const string ApplicationName = "CloudScout";
    private const string GoogleAppsMimePrefix = "application/vnd.google-apps";

    private readonly GoogleCredentialFactory _credentialFactory;
    private readonly ILogger<GoogleDriveProvider> _logger;

    public GoogleDriveProvider(GoogleCredentialFactory credentialFactory, ILogger<GoogleDriveProvider> logger)
    {
        _credentialFactory = credentialFactory;
        _logger = logger;
    }

    public string ProviderName => "GoogleDrive";

    /// <summary>
    /// Build a child path by appending <paramref name="name"/> to <paramref name="currentPath"/>,
    /// avoiding a double slash when the parent is the root. Extracted for unit testing — the
    /// rest of the enumeration loop is wrapped around live API calls that are heavy to stub.
    /// </summary>
    internal static string BuildChildPath(string currentPath, string name) =>
        currentPath == "/" ? $"/{name}" : $"{currentPath}/{name}";

    public async Task<ConnectionResult> AuthenticateInteractiveAsync(
        Func<string, Task>? deviceCodePrompt = null,
        CancellationToken cancellationToken = default)
    {
        // Google's loopback flow opens a browser; deviceCodePrompt is unused for this provider.
        var credential = await _credentialFactory.AuthorizeAsync("default", cancellationToken).ConfigureAwait(false);

        var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
        var aboutRequest = drive.About.Get();
        aboutRequest.Fields = "user(emailAddress)";
        var about = await aboutRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        var email = about.User?.EmailAddress
            ?? throw new InvalidOperationException("Google OAuth succeeded but Drive About.User returned no email.");

        _logger.LogInformation("Authenticated Google Drive account {Email}", email);

        // Email doubles as both display value and stable per-account key. The FileDataStore was
        // initially keyed under "default"; rename the cache file so future LoadAsync(email) finds it.
        ReKeyCache("default", email);

        return new ConnectionResult(email, email);
    }

    public async IAsyncEnumerable<RemoteFileMetadata> EnumerateFilesAsync(
        string homeAccountId,
        string? resumeFromPath = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var drive = await BuildDriveServiceAsync(homeAccountId, cancellationToken).ConfigureAwait(false);

        // BFS: queue holds (parent folder id, path so far). Drive's "root" alias targets My Drive.
        var queue = new Queue<(string Id, string Path)>();
        queue.Enqueue(("root", "/"));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folderId, currentPath) = queue.Dequeue();

            await foreach (var item in EnumerateFolderChildrenAsync(drive, folderId, cancellationToken).ConfigureAwait(false))
            {
                var name = item.Name ?? string.Empty;
                var childPath = BuildChildPath(currentPath, name);

                if (item.MimeType == "application/vnd.google-apps.folder")
                {
                    if (!string.IsNullOrEmpty(item.Id))
                        queue.Enqueue((item.Id, childPath));
                    continue;
                }

                // Skip Google Workspace native docs (Docs/Sheets/Slides) — they need .Export rather
                // than .Get and we don't have a use case for that yet. Other vnd.google-apps types
                // (forms, drawings) are also out of scope.
                if (item.MimeType is not null && item.MimeType.StartsWith(GoogleAppsMimePrefix, StringComparison.Ordinal))
                    continue;

                if (resumeFromPath is not null &&
                    string.CompareOrdinal(childPath, resumeFromPath) <= 0)
                {
                    continue;
                }

                yield return new RemoteFileMetadata(
                    ExternalFileId: item.Id ?? string.Empty,
                    ExternalPath: childPath,
                    FileName: name,
                    ParentFolderPath: currentPath,
                    MimeType: item.MimeType,
                    SizeBytes: item.Size ?? 0L,
                    CreatedUtc: item.CreatedTimeDateTimeOffset?.UtcDateTime,
                    ModifiedUtc: item.ModifiedTimeDateTimeOffset?.UtcDateTime);
            }
        }
    }

    public async Task<Stream> DownloadAsync(
        string homeAccountId,
        string externalFileId,
        CancellationToken cancellationToken = default)
    {
        var drive = await BuildDriveServiceAsync(homeAccountId, cancellationToken).ConfigureAwait(false);

        var buffer = new MemoryStream();
        await drive.Files.Get(externalFileId).DownloadAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }

    private async Task<DriveService> BuildDriveServiceAsync(string userKey, CancellationToken ct)
    {
        var credential = await _credentialFactory.LoadAsync(userKey, ct).ConfigureAwait(false);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
    }

    private static async IAsyncEnumerable<DriveFile> EnumerateFolderChildrenAsync(
        DriveService drive,
        string folderId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? pageToken = null;
        do
        {
            var request = drive.Files.List();
            request.Q = $"'{folderId}' in parents and trashed = false";
            request.Fields = "nextPageToken, files(id, name, mimeType, size, createdTime, modifiedTime)";
            request.PageSize = 1000;
            request.PageToken = pageToken;
            request.SupportsAllDrives = false;

            var response = await request.ExecuteAsync(ct).ConfigureAwait(false);
            if (response.Files is not null)
            {
                foreach (var f in response.Files)
                    yield return f;
            }
            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

    private void ReKeyCache(string fromKey, string toKey)
    {
        // FileDataStore stores tokens at <cacheDir>/Google.Apis.Auth.OAuth2.Responses.TokenResponse-<key>.
        // After first auth we know the stable key (email), so move the file so silent reloads work.
        try
        {
            // FileDataStore (with fullPath: true) uses the cacheDir as-is; the file name format is the
            // type's full name + "-" + the userKey (no extension).
            var dir = new DirectoryInfo(_credentialFactory.CacheDirectory);
            if (!dir.Exists) return;

            const string tokenTypeName = "Google.Apis.Auth.OAuth2.Responses.TokenResponse";
            var src = Path.Combine(dir.FullName, $"{tokenTypeName}-{fromKey}");
            var dst = Path.Combine(dir.FullName, $"{tokenTypeName}-{toKey}");

            if (System.IO.File.Exists(src) && !System.IO.File.Exists(dst))
                System.IO.File.Move(src, dst);
        }
        catch (Exception ex)
        {
            // Non-fatal: worst case the user re-auths next time. Logged for diagnostics.
            _logger.LogWarning(ex, "Failed to re-key Google token cache from {From} to {To}", fromKey, toKey);
        }
    }

}
