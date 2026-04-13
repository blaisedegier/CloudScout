namespace CloudScout.Core.Taxonomy;

/// <summary>
/// A complete taxonomy: name, version, and the set of categories that classifiers score against.
/// Immutable once loaded — <see cref="JsonTaxonomyLoader"/> is the canonical construction path.
/// </summary>
public sealed class Taxonomy
{
    private readonly Dictionary<string, TaxonomyCategory> _categoriesById;

    public Taxonomy(string name, string version, IReadOnlyList<TaxonomyCategory> categories)
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
