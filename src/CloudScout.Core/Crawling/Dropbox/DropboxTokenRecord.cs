namespace CloudScout.Core.Crawling.Dropbox;

/// <summary>
/// On-disk representation of a Dropbox account's persisted credentials. One file per account
/// under <see cref="DropboxAuthOptions.ResolveCacheDirectory"/>, named after <see cref="AccountId"/>.
/// </summary>
public sealed class DropboxTokenRecord
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}
