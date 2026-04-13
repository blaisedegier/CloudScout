using System.Collections.Concurrent;
using System.Reflection;

namespace CloudScout.Core.Taxonomy;

/// <summary>
/// Default <see cref="ITaxonomyProvider"/>. Looks up a name by first trying the built-in
/// embedded resources in the Core assembly (e.g. <c>generic-default</c>), then falling back to
/// treating the argument as a filesystem path. Successfully loaded taxonomies are cached for
/// the lifetime of the provider instance.
/// </summary>
public sealed class EmbeddedAndFileTaxonomyProvider : ITaxonomyProvider
{
    // Namespace prefix under which the Taxonomies/*.json files are packed as embedded resources.
    // Matches the folder structure in Core and the csproj <EmbeddedResource Include> glob.
    private const string EmbeddedResourcePrefix = "CloudScout.Core.Taxonomy.Taxonomies.";

    private readonly ConcurrentDictionary<string, Taxonomy> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Assembly _resourceAssembly;

    public EmbeddedAndFileTaxonomyProvider() : this(typeof(EmbeddedAndFileTaxonomyProvider).Assembly)
    {
    }

    // Exposed for tests that want to substitute a test assembly.
    internal EmbeddedAndFileTaxonomyProvider(Assembly resourceAssembly)
    {
        _resourceAssembly = resourceAssembly;
    }

    public async Task<Taxonomy> GetAsync(string nameOrPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            throw new ArgumentException("Taxonomy name or path is required.", nameof(nameOrPath));

        if (_cache.TryGetValue(nameOrPath, out var cached))
            return cached;

        var taxonomy = await LoadAsync(nameOrPath, cancellationToken).ConfigureAwait(false);
        _cache[nameOrPath] = taxonomy;
        return taxonomy;
    }

    private async Task<Taxonomy> LoadAsync(string nameOrPath, CancellationToken ct)
    {
        // Try embedded resource first — short, well-known names like "generic-default" resolve here.
        var resourceName = $"{EmbeddedResourcePrefix}{nameOrPath}.json";
        var embedded = _resourceAssembly.GetManifestResourceStream(resourceName);
        if (embedded is not null)
        {
            await using (embedded)
            {
                return await JsonTaxonomyLoader.LoadFromStreamAsync(embedded, resourceName, ct).ConfigureAwait(false);
            }
        }

        // Fall back to treating the argument as a file path.
        if (File.Exists(nameOrPath))
            return await JsonTaxonomyLoader.LoadFromFileAsync(nameOrPath, ct).ConfigureAwait(false);

        var available = ListEmbeddedTaxonomyNames(_resourceAssembly);
        throw new InvalidOperationException(
            $"Taxonomy '{nameOrPath}' not found. Expected a built-in name (one of: {string.Join(", ", available)}) " +
            $"or an existing path to a JSON file.");
    }

    private static IEnumerable<string> ListEmbeddedTaxonomyNames(Assembly assembly) =>
        assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n.Substring(EmbeddedResourcePrefix.Length, n.Length - EmbeddedResourcePrefix.Length - ".json".Length))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
}
