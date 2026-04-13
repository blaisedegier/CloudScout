using CloudScout.Core.Taxonomy;
using Microsoft.Extensions.Logging;

namespace CloudScout.Core.Classification;

/// <summary>
/// Thin orchestrator over registered <see cref="IClassificationTier"/> instances. The pipeline
/// doesn't decide <i>when</i> tiers should run — that lives in the scan orchestrator, which has
/// the I/O context to trade off cost (download, text extraction, LLM inference) against the
/// confidence already in hand. The pipeline just exposes each tier by number and centralises
/// logging.
/// </summary>
public sealed class ClassificationPipeline
{
    private readonly IReadOnlyDictionary<int, IClassificationTier> _tiersByNumber;
    private readonly ILogger<ClassificationPipeline> _logger;

    public ClassificationPipeline(IEnumerable<IClassificationTier> tiers, ILogger<ClassificationPipeline> logger)
    {
        _tiersByNumber = tiers.ToDictionary(t => t.Tier);
        _logger = logger;
    }

    /// <summary>The tier numbers currently registered in the DI container, in ascending order.</summary>
    public IReadOnlyList<int> AvailableTiers => _tiersByNumber.Keys.OrderBy(x => x).ToList();

    /// <summary>
    /// Run a single named tier against the context. Returns empty if the requested tier isn't
    /// registered (rather than throwing) so callers can probe optimistically, e.g. try Tier 3
    /// only when Phase C is deployed.
    /// </summary>
    public async Task<IReadOnlyList<ClassificationResult>> RunTierAsync(
        int tierNumber,
        ClassificationContext context,
        TaxonomyDefinition taxonomy,
        CancellationToken cancellationToken = default)
    {
        if (!_tiersByNumber.TryGetValue(tierNumber, out var tier))
        {
            _logger.LogDebug("Requested tier {Tier} is not registered; skipping.", tierNumber);
            return Array.Empty<ClassificationResult>();
        }

        return await tier.ClassifyAsync(context, taxonomy, cancellationToken).ConfigureAwait(false);
    }
}
