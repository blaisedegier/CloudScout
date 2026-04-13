namespace CloudScout.Core.Crawling;

/// <summary>
/// Configuration for the Microsoft Entra ID app registration used to authenticate
/// against OneDrive. Populated from the "Authentication:Microsoft" configuration section.
/// </summary>
public class MicrosoftAuthOptions
{
    public const string SectionName = "Authentication:Microsoft";

    /// <summary>Application (client) ID from the Entra ID app registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Tenant identifier. Use "common" to support both work/school and personal Microsoft accounts,
    /// "organizations" for work/school only, or a specific tenant GUID to restrict access.
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Scopes requested during authentication. "Files.Read" is the minimum needed to enumerate
    /// and download files; "offline_access" is required for refresh tokens.
    /// </summary>
    public string[] Scopes { get; set; } = new[] { "Files.Read", "User.Read", "offline_access" };

    /// <summary>
    /// File name (within <see cref="CacheDirectory"/>) where MSAL persists its encrypted token cache.
    /// </summary>
    public string CacheFileName { get; set; } = "msal-cache.bin";

    /// <summary>
    /// Absolute path to the directory where the MSAL token cache file is stored. If empty (default),
    /// resolves at runtime to <c>%LocalAppData%\CloudScout</c> on Windows.
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    public string Authority => $"https://login.microsoftonline.com/{TenantId}";

    public string ResolveCacheDirectory()
    {
        if (!string.IsNullOrWhiteSpace(CacheDirectory))
            return CacheDirectory;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "CloudScout");
    }
}
