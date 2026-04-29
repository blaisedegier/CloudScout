namespace CloudScout.Core.Crawling.GoogleDrive;

/// <summary>
/// Configuration for the Google Cloud Console OAuth client used to authenticate against
/// Google Drive. Populated from the "Authentication:Google" configuration section.
/// </summary>
public class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    /// <summary>OAuth 2.0 client ID (Desktop app) from Google Cloud Console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth 2.0 client secret. Per Google's docs, secrets for installed/desktop apps are not
    /// truly confidential — they're identifiers paired with the client ID. They still belong
    /// in the gitignored appsettings.Local.json, never in committed config.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Scopes requested at consent. <c>drive.readonly</c> covers enumeration + download;
    /// <c>userinfo.email</c> lets us resolve the signed-in account's email for display.
    /// </summary>
    public string[] Scopes { get; set; } = new[]
    {
        "https://www.googleapis.com/auth/drive.readonly",
        "https://www.googleapis.com/auth/userinfo.email",
    };

    /// <summary>
    /// Absolute path to the directory where Google's <c>FileDataStore</c> persists refresh tokens.
    /// Empty (default) resolves to <c>%LocalAppData%\CloudScout\google-tokens</c>.
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    public string ResolveCacheDirectory()
    {
        if (!string.IsNullOrWhiteSpace(CacheDirectory))
            return CacheDirectory;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "CloudScout", "google-tokens");
    }
}
