using global::Dropbox.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace CloudScout.Core.Crawling.Dropbox;

/// <summary>
/// Drives the Dropbox PKCE OAuth flow and persists refresh tokens to a per-account JSON file.
/// Equivalent role to <see cref="GoogleDrive.GoogleCredentialFactory"/> and the MSAL factory
/// for OneDrive — abstracts the auth dance away from the provider.
/// </summary>
public sealed class DropboxCredentialFactory
{
    private readonly DropboxAuthOptions _options;
    private readonly ILogger<DropboxCredentialFactory> _logger;

    public DropboxCredentialFactory(IOptions<DropboxAuthOptions> options, ILogger<DropboxCredentialFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Run the interactive PKCE flow: open the user's browser to Dropbox's consent page, capture
    /// the redirect on a localhost listener, exchange the code for tokens, persist them, return
    /// the resulting record.
    /// </summary>
    public async Task<DropboxTokenRecord> AuthorizeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AppKey))
            throw new InvalidOperationException(
                $"Missing {DropboxAuthOptions.SectionName}:AppKey. Set it in appsettings.Local.json — see docs/setup.md.");

        var (listener, redirectUri) = StartLoopbackListener();
        try
        {
            var pkce = new PKCEOAuthFlow();
            var state = Guid.NewGuid().ToString("N");

            // PKCEOAuthFlow holds the code verifier internally; the matching ProcessCodeFlowAsync
            // overload below picks it up automatically.
            var authUri = pkce.GetAuthorizeUri(
                OAuthResponseType.Code,
                _options.AppKey,
                redirectUri,
                state: state,
                tokenAccessType: TokenAccessType.Offline);

            OpenBrowser(authUri.ToString());

            var context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var query = context.Request.Url?.Query ?? string.Empty;
            await RespondAsync(context.Response, "Authentication complete. You can close this tab.").ConfigureAwait(false);

            var parsed = System.Web.HttpUtility.ParseQueryString(query);
            var code = parsed["code"];
            var returnedState = parsed["state"];

            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException($"Dropbox redirect missing 'code' parameter: {query}");
            if (returnedState != state)
                throw new InvalidOperationException("Dropbox OAuth state mismatch — possible CSRF, aborting.");

            var token = await pkce.ProcessCodeFlowAsync(
                code,
                _options.AppKey,
                redirectUri: redirectUri).ConfigureAwait(false);

            // Resolve email + accountId via a one-shot DropboxClient using the access token.
            using var probe = new DropboxClient(token.AccessToken);
            var account = await probe.Users.GetCurrentAccountAsync().ConfigureAwait(false);

            var record = new DropboxTokenRecord
            {
                AccountId = account.AccountId,
                Email = account.Email,
                RefreshToken = token.RefreshToken
                    ?? throw new InvalidOperationException("Dropbox token response missing refresh_token — verify TokenAccessType.Offline."),
            };

            await SaveAsync(record, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Dropbox OAuth flow completed for {Email}", record.Email);
            return record;
        }
        finally
        {
            listener.Stop();
            ((IDisposable)listener).Dispose();
        }
    }

    /// <summary>
    /// Build a refresh-token-backed <see cref="DropboxClient"/> for an account that has already
    /// been authorised. The SDK refreshes the access token automatically when it expires.
    /// </summary>
    public async Task<DropboxClient> CreateClientAsync(string accountId, CancellationToken cancellationToken)
    {
        var record = await LoadAsync(accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No cached Dropbox credentials for account '{accountId}'. Run `cloudscout connect dropbox` first.");

        // DropboxClientConfig defaults are fine; we only need the app key + refresh token to let
        // the SDK refresh access tokens transparently on each call.
        return new DropboxClient(record.RefreshToken, _options.AppKey);
    }

    public async Task<DropboxTokenRecord?> LoadAsync(string accountId, CancellationToken cancellationToken)
    {
        var path = TokenFilePath(accountId);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DropboxTokenRecord>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(DropboxTokenRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.ResolveCacheDirectory());
        var path = TokenFilePath(record.AccountId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, record, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private string TokenFilePath(string accountId)
    {
        // Account IDs from Dropbox look like "dbid:AAH4f...". Sanitise to a flat filename.
        var safe = accountId.Replace(':', '_');
        return Path.Combine(_options.ResolveCacheDirectory(), $"{safe}.json");
    }

    private (HttpListener listener, string redirectUri) StartLoopbackListener()
    {
        for (int port = _options.LoopbackPortStart; port <= _options.LoopbackPortEnd; port++)
        {
            var prefix = $"http://localhost:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                return (listener, prefix);
            }
            catch (HttpListenerException)
            {
                ((IDisposable)listener).Dispose();
            }
        }
        throw new InvalidOperationException(
            $"Could not bind any loopback port in range {_options.LoopbackPortStart}-{_options.LoopbackPortEnd} for Dropbox OAuth redirect.");
    }

    private static async Task RespondAsync(HttpListenerResponse response, string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"<html><body><p>{body}</p></body></html>");
        response.ContentType = "text/html";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static void OpenBrowser(string url)
    {
        // ProcessStartInfo with UseShellExecute = true is the cross-platform way to launch the
        // default browser. Throws on environments without a UI (e.g. headless CI) — that's a
        // user-environment issue, not something we can recover from in code.
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
