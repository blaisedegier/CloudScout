namespace CloudScout.Core.Taxonomy;

/// <summary>
/// Resolves a taxonomy by name (for built-in taxonomies embedded in the Core assembly) or by
/// file path (for user-supplied overrides). Implementations cache loaded taxonomies so repeated
/// lookups don't re-parse JSON.
/// </summary>
public interface ITaxonomyProvider
{
    Task<TaxonomyDefinition> GetAsync(string nameOrPath, CancellationToken cancellationToken = default);
}
