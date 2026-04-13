using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;

namespace CloudScout.Core.Crawling.Authentication;

/// <summary>
/// Kiota <see cref="IAccessTokenProvider"/> that acquires tokens silently from an MSAL public
/// client's encrypted cache. Used to back the Microsoft.Graph SDK's authentication pipeline
/// for a specific cached account.
/// </summary>
internal sealed class MsalAccessTokenProvider : IAccessTokenProvider
{
    private readonly IPublicClientApplication _app;
    private readonly string _homeAccountId;
    private readonly string[] _scopes;

    public MsalAccessTokenProvider(IPublicClientApplication app, string homeAccountId, string[] scopes)
    {
        _app = app;
        _homeAccountId = homeAccountId;
        _scopes = scopes;
    }

    public AllowedHostsValidator AllowedHostsValidator { get; } = new();

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        var accounts = await _app.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault(a => a.HomeAccountId.Identifier == _homeAccountId)
            ?? throw new InvalidOperationException(
                $"No cached MSAL account with HomeAccountId='{_homeAccountId}'. Run 'cloudscout connect onedrive' first.");

        var result = await _app
            .AcquireTokenSilent(_scopes, account)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return result.AccessToken;
    }
}
