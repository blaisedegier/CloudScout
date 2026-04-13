using CloudScout.Core.Crawling.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using System.Runtime.CompilerServices;

namespace CloudScout.Core.Crawling.OneDrive;

/// <summary>
/// <see cref="ICloudStorageProvider"/> implementation backed by Microsoft Graph and the
/// signed-in user's OneDrive. Authentication uses the MSAL device-code flow so no redirect
/// URI needs to be configured on the Entra ID app registration for CLI usage.
/// </summary>
public sealed class OneDriveProvider : ICloudStorageProvider
{
    // The Graph REST API accepts the literal string "root" as an item ID to target the drive root.
    private const string RootItemId = "root";

    private readonly MsalPublicClientFactory _clientFactory;
    private readonly MicrosoftAuthOptions _options;
    private readonly ILogger<OneDriveProvider> _logger;

    public OneDriveProvider(
        MsalPublicClientFactory clientFactory,
        IOptions<MicrosoftAuthOptions> options,
        ILogger<OneDriveProvider> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderName => "OneDrive";

    public async Task<ConnectionResult> AuthenticateInteractiveAsync(
        Func<string, Task>? deviceCodePrompt = null,
        CancellationToken cancellationToken = default)
    {
        var app = await _clientFactory.GetAsync(cancellationToken).ConfigureAwait(false);

        var result = await app
            .AcquireTokenWithDeviceCode(_options.Scopes, async dcr =>
            {
                if (deviceCodePrompt is not null)
                    await deviceCodePrompt(dcr.Message).ConfigureAwait(false);
                else
                    Console.WriteLine(dcr.Message);
            })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        var account = result.Account
            ?? throw new InvalidOperationException("Device-code authentication returned no account.");

        _logger.LogInformation("Authenticated OneDrive account {Username} (homeAccountId={HomeAccountId})",
            account.Username, account.HomeAccountId.Identifier);

        return new ConnectionResult(account.Username, account.HomeAccountId.Identifier);
    }

    public async IAsyncEnumerable<RemoteFileMetadata> EnumerateFilesAsync(
        string homeAccountId,
        string? resumeFromPath = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var graph = await BuildGraphClientAsync(homeAccountId, cancellationToken).ConfigureAwait(false);
        var driveId = await GetDefaultDriveIdAsync(graph, cancellationToken).ConfigureAwait(false);

        // Breadth-first traversal. Queue holds (driveItemId, folderPathSoFar).
        var queue = new Queue<(string Id, string Path)>();
        queue.Enqueue((RootItemId, "/"));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (id, currentPath) = queue.Dequeue();

            await foreach (var item in EnumerateFolderChildrenAsync(graph, driveId, id, cancellationToken).ConfigureAwait(false))
            {
                var childPath = currentPath == "/" ? $"/{item.Name}" : $"{currentPath}/{item.Name}";

                if (item.Folder is not null)
                {
                    if (item.Id is not null)
                        queue.Enqueue((item.Id, childPath));
                    continue;
                }

                if (item.File is null) continue; // skip other kinds (notebooks, shortcuts, etc.)

                // Resume filter: skip paths that lexicographically precede resumeFromPath.
                if (resumeFromPath is not null &&
                    string.CompareOrdinal(childPath, resumeFromPath) <= 0)
                {
                    continue;
                }

                yield return new RemoteFileMetadata(
                    ExternalFileId: item.Id ?? string.Empty,
                    ExternalPath: childPath,
                    FileName: item.Name ?? string.Empty,
                    ParentFolderPath: currentPath,
                    MimeType: item.File.MimeType,
                    SizeBytes: item.Size ?? 0L,
                    CreatedUtc: item.CreatedDateTime?.UtcDateTime,
                    ModifiedUtc: item.LastModifiedDateTime?.UtcDateTime);
            }
        }
    }

    public async Task<Stream> DownloadAsync(
        string homeAccountId,
        string externalFileId,
        CancellationToken cancellationToken = default)
    {
        var graph = await BuildGraphClientAsync(homeAccountId, cancellationToken).ConfigureAwait(false);
        var driveId = await GetDefaultDriveIdAsync(graph, cancellationToken).ConfigureAwait(false);

        var stream = await graph.Drives[driveId].Items[externalFileId].Content
            .GetAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return stream ?? throw new InvalidOperationException(
            $"Download returned no content for file id '{externalFileId}'.");
    }

    private async Task<GraphServiceClient> BuildGraphClientAsync(string homeAccountId, CancellationToken ct)
    {
        var app = await _clientFactory.GetAsync(ct).ConfigureAwait(false);
        var tokenProvider = new MsalAccessTokenProvider(app, homeAccountId, _options.Scopes);
        var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }

    private static async Task<string> GetDefaultDriveIdAsync(GraphServiceClient graph, CancellationToken ct)
    {
        var drive = await graph.Me.Drive.GetAsync(cancellationToken: ct).ConfigureAwait(false);
        return drive?.Id
            ?? throw new InvalidOperationException("Could not resolve the signed-in user's default drive ID.");
    }

    private static async IAsyncEnumerable<DriveItem> EnumerateFolderChildrenAsync(
        GraphServiceClient graph,
        string driveId,
        string driveItemId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var page = await graph.Drives[driveId].Items[driveItemId].Children
            .GetAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        while (page is not null)
        {
            if (page.Value is not null)
            {
                foreach (var item in page.Value)
                    yield return item;
            }

            if (string.IsNullOrEmpty(page.OdataNextLink)) break;

            // Follow pagination via the @odata.nextLink URL. WithUrl() returns a request builder
            // of the same shape configured against the arbitrary URL.
            page = await graph.Drives[driveId].Items[driveItemId].Children
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: ct)
                .ConfigureAwait(false);
        }
    }
}
