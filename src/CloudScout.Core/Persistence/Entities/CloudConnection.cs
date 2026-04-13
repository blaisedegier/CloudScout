namespace CloudScout.Core.Persistence.Entities;

/// <summary>
/// An authenticated connection to a cloud storage provider. Stores the encrypted
/// refresh token required to re-acquire access tokens without prompting the user.
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
    /// Refresh token encrypted at rest via <see cref="System.Security.Cryptography.ProtectedData"/> (DPAPI, Windows-scoped).
    /// Never persisted in plaintext. Rotated automatically when the provider issues a new refresh token.
    /// </summary>
    public byte[] EncryptedRefreshToken { get; set; } = Array.Empty<byte>();

    /// <summary>When the most recently issued access token expires. Informational only — refresh is always attempted on use.</summary>
    public DateTime? TokenExpiresUtc { get; set; }

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
