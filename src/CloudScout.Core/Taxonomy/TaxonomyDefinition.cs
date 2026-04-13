namespace CloudScout.Core.Taxonomy;

/// <summary>
/// A complete, loaded taxonomy: name, version, and the set of categories that classifiers score
/// against. Immutable once constructed — <see cref="JsonTaxonomyLoader"/> is the canonical
/// construction path. Named <c>TaxonomyDefinition</c> rather than <c>Taxonomy</c> because the
/// enclosing namespace is already <c>Taxonomy</c>.
/// </summary>
public sealed class TaxonomyDefinition
{
    private readonly Dictionary<string, TaxonomyCategory> _categoriesById;

    public TaxonomyDefinition(string name, string version, IReadOnlyList<TaxonomyCategory> categories)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Taxonomy name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Taxonomy version is required.", nameof(version));
        ArgumentNullException.ThrowIfNull(categories);

        Name = name;
        Version = version;
        Categories = categories;
        _categoriesById = categories.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }
    public string Version { get; }
    public IReadOnlyList<TaxonomyCategory> Categories { get; }

    public TaxonomyCategory? GetById(string id) =>
        _categoriesById.TryGetValue(id, out var category) ? category : null;
}
