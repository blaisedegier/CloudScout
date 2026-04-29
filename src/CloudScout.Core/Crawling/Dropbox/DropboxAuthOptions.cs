namespace CloudScout.Core.Crawling.Dropbox;

/// <summary>
/// Configuration for the Dropbox app used to authenticate against a user's personal Dropbox.
/// Populated from the "Authentication:Dropbox" configuration section. PKCE replaces the old
/// app-secret flow, so only the public app key is needed here.
/// </summary>
public class DropboxAuthOptions
{
    public const string SectionName = "Authentication:Dropbox";

    /// <summary>App key from the Dropbox App Console (the "client ID" equivalent).</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>
    /// Loopback port range used by the OAuth redirect listener. We pick the first free port in
    /// this range and register <c>http://127.0.0.1:&lt;port&gt;/</c> as the redirect URI.
    /// </summary>
    public int LoopbackPortStart { get; set; } = 53682;
    public int LoopbackPortEnd { get; set; } = 53692;

    /// <summary>
    /// Absolute path to the directory where refresh tokens are persisted, one file per account.
    /// Empty (default) resolves to <c>%LocalAppData%\CloudScout\dropbox-tokens</c>.
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    public string ResolveCacheDirectory()
    {
        if (!string.IsNullOrWhiteSpace(CacheDirectory))
            return CacheDirectory;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "CloudScout", "dropbox-tokens");
    }
}
