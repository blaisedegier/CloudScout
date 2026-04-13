namespace CloudScout.Core.Crawling;

/// <summary>
/// Result of a successful interactive authentication against a cloud provider.
/// The caller persists these values as a <see cref="Persistence.Entities.CloudConnection"/> row.
/// </summary>
/// <param name="AccountIdentifier">Human-readable display name (typically the account's email address).</param>
/// <param name="HomeAccountId">Stable identifier used to retrieve the cached account on subsequent scans.</param>
public sealed record ConnectionResult(string AccountIdentifier, string HomeAccountId);
