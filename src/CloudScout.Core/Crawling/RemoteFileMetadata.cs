namespace CloudScout.Core.Crawling;

/// <summary>
/// Provider-agnostic representation of a single file discovered during enumeration.
/// Does not include content — content is fetched lazily via
/// <see cref="ICloudStorageProvider.DownloadAsync"/> when a classifier needs it.
/// </summary>
public sealed record RemoteFileMetadata(
    string ExternalFileId,
    string ExternalPath,
    string FileName,
    string ParentFolderPath,
    string? MimeType,
    long SizeBytes,
    DateTime? CreatedUtc,
    DateTime? ModifiedUtc);
