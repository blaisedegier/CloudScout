using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace CloudScout.Core.Crawling.Authentication;

/// <summary>
/// Builds and caches a single <see cref="IPublicClientApplication"/> instance configured with
/// a file-backed encrypted token cache via <c>Microsoft.Identity.Client.Extensions.Msal</c>.
/// The cache file is DPAPI-encrypted on Windows.
/// </summary>
public sealed class MsalPublicClientFactory : IAsyncDisposable
{
    private readonly MicrosoftAuthOptions _options;
    private readonly ILogger<MsalPublicClientFactory> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _app;
    private MsalCacheHelper? _cacheHelper;

    public MsalPublicClientFactory(IOptions<MicrosoftAuthOptions> options, ILogger<MsalPublicClientFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IPublicClientApplication> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return _app;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_app is not null) return _app;

            if (string.IsNullOrWhiteSpace(_options.ClientId))
                throw new InvalidOperationException(
                    $"Missing {MicrosoftAuthOptions.SectionName}:ClientId. Set it in appsettings.Local.json — see docs/setup.md.");

            var cacheDir = _options.ResolveCacheDirectory();
            Directory.CreateDirectory(cacheDir);

            var storage = new StorageCreationPropertiesBuilder(_options.CacheFileName, cacheDir)
                .Build();

            _cacheHelper = await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);

            var app = PublicClientApplicationBuilder
                .Create(_options.ClientId)
                .WithAuthority(_options.Authority)
                .WithDefaultRedirectUri()
                .Build();

            _cacheHelper.RegisterCache(app.UserTokenCache);
            _app = app;

            _logger.LogInformation("MSAL public client initialized (authority={Authority}, cache={CacheDir})",
                _options.Authority, cacheDir);

            return _app;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
