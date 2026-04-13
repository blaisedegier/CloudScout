using CloudScout.Core.Taxonomy;

namespace CloudScout.Core.Classification;

/// <summary>
/// A single tier in the classification pipeline. Each tier independently scores a file against
/// every category in the taxonomy and returns any candidates that produced a non-zero score.
/// The pipeline orchestrates tiers in increasing cost order and may short-circuit when an earlier
/// tier produces sufficient confidence.
/// </summary>
public interface IClassificationTier
{
    /// <summary>Tier number: 0 = metadata heuristics, 1 = keyword on extracted text, 3 = LLM.</summary>
    int Tier { get; }

    /// <summary>
    /// Evaluate this tier against the file represented by <paramref name="context"/> and the
    /// categories in <paramref name="taxonomy"/>. Returns zero or more candidate results ordered
    /// by descending confidence. May be async if a tier needs to download content or invoke a model.
    /// </summary>
    Task<IReadOnlyList<ClassificationResult>> ClassifyAsync(
        ClassificationContext context,
        TaxonomyDefinition taxonomy,
        CancellationToken cancellationToken = default);
}
