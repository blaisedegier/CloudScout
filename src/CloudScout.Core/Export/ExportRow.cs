namespace CloudScout.Core.Export;

public sealed record ExportRow(
    string ExternalPath,
    long SizeBytes,
    string? CategoryId,
    string? CategoryDisplayName,
    double? Confidence,
    int? Tier,
    string? Reason);
