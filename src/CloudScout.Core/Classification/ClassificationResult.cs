namespace CloudScout.Core.Classification;

/// <summary>
/// One candidate classification produced by a single tier for a single file. The pipeline
/// collects these across tiers and lets the caller decide which to persist / show.
/// </summary>
public sealed record ClassificationResult(
    string CategoryId,
    double ConfidenceScore,
    int Tier,
    string Reason);
