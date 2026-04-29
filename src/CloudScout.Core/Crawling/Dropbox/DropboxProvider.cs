using global::Dropbox.Api;
using global::Dropbox.Api.Files;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace CloudScout.Core.Crawling.Dropbox;

/// <summary>
/// <see cref="ICloudStorageProvider"/> implementation backed by Dropbox v2. Dropbox exposes
/// native paths and a recursive list endpoint, so enumeration is a single paged call rather
/// than a BFS walk.
/// </summary>
public sealed class DropboxProvider : ICloudStorageProvider
{
    private readonly DropboxCredentialFactory _credentialFactory;
    private readonly ILogger<DropboxProvider> _logger;

    public DropboxProvider(DropboxCredentialFactory credentialFactory, ILogger<DropboxProvider> logger)
    {
        _credentialFactory = credentialFactory;
        _logger = logger;
    }

    public string ProviderName => "Dropbox";

    public async Task<ConnectionResult> AuthenticateInteractiveAsync(
        Func<string, Task>? deviceCodePrompt = null,
        CancellationToken cancellationToken = default)
    {
        var record = await _credentialFactory.AuthorizeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Authenticated Dropbox account {Email}", record.Email);
        return new ConnectionResult(record.Email, record.AccountId);
    }

    public async IAsyncEnumerable<RemoteFileMetadata> EnumerateFilesAsync(
        string homeAccountId,
        string? resumeFromPath = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var client = await _credentialFactory.CreateClientAsync(homeAccountId, cancellationToken).ConfigureAwait(false);

        // Empty path means "root of My Files". recursive: true expands the entire tree, paginated
        // via continuation cursors.
        var result = await client.Files.ListFolderAsync(string.Empty, recursive: true).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var entry in result.Entries)
            {
                if (!entry.IsFile) continue;
                var file = entry.AsFile;

                var path = file.PathDisplay ?? file.PathLower ?? string.Empty;
                if (resumeFromPath is not null &&
                    string.CompareOrdinal(path, resumeFromPath) <= 0)
                {
                    continue;
                }

                var parent = ParentPath(path);

                yield return new RemoteFileMetadata(
                    ExternalFileId: file.Id,
                    ExternalPath: path,
                    FileName: file.Name,
                    ParentFolderPath: parent,
                    MimeType: null, // Dropbox doesn't surface MIME in metadata; downstream code keys off file extension.
                    SizeBytes: (long)file.Size,
                    CreatedUtc: null,
                    ModifiedUtc: file.ServerModified);
            }

            if (!result.HasMore) break;
            result = await client.Files.ListFolderContinueAsync(result.Cursor).ConfigureAwait(false);
        }
    }

    public async Task<Stream> DownloadAsync(
        string homeAccountId,
        string externalFileId,
        CancellationToken cancellationToken = default)
    {
        using var client = await _credentialFactory.CreateClientAsync(homeAccountId, cancellationToken).ConfigureAwait(false);

        // Dropbox accepts either a path or an "id:..." reference. File IDs from ListFolder are the
        // stable form, so use them directly.
        using var response = await client.Files.DownloadAsync(externalFileId).ConfigureAwait(false);
        var bytes = await response.GetContentAsByteArrayAsync().ConfigureAwait(false);
        return new MemoryStream(bytes);
    }

    internal static string ParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        var idx = path.LastIndexOf('/');
        if (idx <= 0) return "/";
        return path[..idx];
    }
}
