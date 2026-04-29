using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudScout.Core.Crawling.GoogleDrive;

/// <summary>
/// Wraps <see cref="GoogleWebAuthorizationBroker"/> and a <see cref="FileDataStore"/> so the
/// rest of the provider can ask for a <see cref="UserCredential"/> without knowing how the
/// loopback OAuth flow or refresh-token persistence work.
/// </summary>
public sealed class GoogleCredentialFactory
{
    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleCredentialFactory> _logger;

    public GoogleCredentialFactory(IOptions<GoogleAuthOptions> options, ILogger<GoogleCredentialFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Resolved absolute path of the on-disk token cache directory.</summary>
    public string CacheDirectory => _options.ResolveCacheDirectory();

    /// <summary>
    /// Run the interactive loopback OAuth flow, opening the user's browser. Returns once the
    /// user has consented and tokens have been persisted to <see cref="FileDataStore"/>.
    /// </summary>
    /// <param name="userKey">
    /// Local cache key. Use a constant (e.g. "default") on first auth — the actual stable identity
    /// (email) becomes available only after the call completes.
    /// </param>
    public async Task<UserCredential> AuthorizeAsync(string userKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException(
                $"Missing {GoogleAuthOptions.SectionName}:ClientId/ClientSecret. Set them in appsettings.Local.json — see docs/setup.md.");

        var cacheDir = _options.ResolveCacheDirectory();
        Directory.CreateDirectory(cacheDir);

        var secrets = new ClientSecrets
        {
            ClientId = _options.ClientId,
            ClientSecret = _options.ClientSecret,
        };

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            _options.Scopes,
            userKey,
            cancellationToken,
            new FileDataStore(cacheDir, fullPath: true)).ConfigureAwait(false);

        _logger.LogInformation("Google OAuth flow completed (cache={CacheDir}, userKey={UserKey})", cacheDir, userKey);
        return credential;
    }

    /// <summary>
    /// Reload an existing credential silently from the on-disk store. Throws if no token has been
    /// cached for the given key — callers should run <see cref="AuthorizeAsync"/> first.
    /// </summary>
    public async Task<UserCredential> LoadAsync(string userKey, CancellationToken cancellationToken)
    {
        // GoogleWebAuthorizationBroker.AuthorizeAsync is the documented entry point for both fresh
        // and refresh flows: if a valid token is already cached for userKey it returns immediately
        // without opening a browser. So reuse it rather than reinventing the silent path.
        return await AuthorizeAsync(userKey, cancellationToken).ConfigureAwait(false);
    }
}
