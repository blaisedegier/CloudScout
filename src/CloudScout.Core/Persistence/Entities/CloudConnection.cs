namespace CloudScout.Core.Persistence.Entities;

/// <summary>
/// An authenticated connection to a cloud storage provider. Identifies which account
/// to use from the MSAL token cache; the actual encrypted token material lives in the
/// cache file managed by <c>Microsoft.Identity.Client.Extensions.Msal</c>.
/// </summary>
public class CloudConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Cloud provider identifier, e.g. "OneDrive". Matches an ICloudStorageProvider registration.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable identifier shown in the UI (typically the account's email address).
    /// Not used for authentication — purely for display.
    /// </summary>
    public string AccountIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// MSAL <c>IAccount.HomeAccountId.Identifier</c> — the stable identifier used to retrieve
    /// the correct account from the encrypted MSAL token cache on subsequent runs. Never
    /// sensitive on its own; the actual tokens live in the OS-encrypted cache file.
    /// </summary>
    public string HomeAccountId { get; set; } = string.Empty;

    public DateTime ConnectedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedUtc { get; set; }

    /// <summary>"Active" or "Revoked". Revoked connections remain in the DB for audit but cannot be used.</summary>
    public string Status { get; set; } = ConnectionStatus.Active;

    // Navigation
    public ICollection<ScanSession> Sessions { get; set; } = new List<ScanSession>();
}

public static class ConnectionStatus
{
    public const string Active = "Active";
    public const string Revoked = "Revoked";
}
