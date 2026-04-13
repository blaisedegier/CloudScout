using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudScout.Core.Taxonomy;

/// <summary>
/// Loads <see cref="Taxonomy"/> instances from JSON. Accepts either a file path or a raw stream
/// (enabling embedded-resource loading from <see cref="LoadFromResource"/> callers). Validates
/// the parsed document before returning — malformed taxonomies fail fast with clear messages.
/// </summary>
public static class JsonTaxonomyLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<TaxonomyDefinition> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Taxonomy file not found: {path}", path);

        await using var stream = File.OpenRead(path);
        return await LoadFromStreamAsync(stream, path, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TaxonomyDefinition> LoadFromStreamAsync(Stream stream, string sourceLabel, CancellationToken cancellationToken = default)
    {
        TaxonomyDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<TaxonomyDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Taxonomy '{sourceLabel}' is not valid JSON: {ex.Message}", ex);
        }

        if (document is null)
            throw new InvalidOperationException($"Taxonomy '{sourceLabel}' deserialized to null.");

        Validate(document, sourceLabel);

        var categories = document.Categories!.Select(ToCategory).ToList();
        return new TaxonomyDefinition(document.Name!, document.Version!, categories);
    }

    private static TaxonomyCategory ToCategory(CategoryDocument source) => new()
    {
        Id = source.Id!,
        DisplayName = source.DisplayName!,
        ParentId = string.IsNullOrWhiteSpace(source.ParentId) ? null : source.ParentId,
        // List<string> and string[] don't unify under ?? without a cast — use the typed conversion
        // to IReadOnlyList<string> (the property type) so either a missing JSON array (null) or
        // a populated one produces a sensible fallback.
        FilenameKeywords = source.FilenameKeywords?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
        FolderKeywords = source.FolderKeywords?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
        ContentKeywords = source.ContentKeywords?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
        ContentPhrases = source.ContentPhrases?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
        NegativeKeywords = source.NegativeKeywords?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
        MimeTypes = source.MimeTypes?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>(),
        BaseConfidence = source.BaseConfidence ?? 0.7,
    };

    private static void Validate(TaxonomyDocument document, string sourceLabel)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(document.Name)) errors.Add("'name' is required");
        if (string.IsNullOrWhiteSpace(document.Version)) errors.Add("'version' is required");
        if (document.Categories is null || document.Categories.Count == 0) errors.Add("'categories' must contain at least one entry");

        if (document.Categories is not null)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < document.Categories.Count; i++)
            {
                var cat = document.Categories[i];
                var position = $"categories[{i}]";

                if (string.IsNullOrWhiteSpace(cat.Id)) errors.Add($"{position}: 'id' is required");
                else if (!seenIds.Add(cat.Id)) errors.Add($"{position}: duplicate id '{cat.Id}'");

                if (string.IsNullOrWhiteSpace(cat.DisplayName)) errors.Add($"{position}: 'displayName' is required");

                if (cat.BaseConfidence is < 0 or > 1)
                    errors.Add($"{position}: 'baseConfidence' must be between 0.0 and 1.0 (got {cat.BaseConfidence})");
            }

            // Second pass so parent references can point forward or backward.
            foreach (var cat in document.Categories.Where(c => !string.IsNullOrWhiteSpace(c.ParentId)))
            {
                if (!seenIds.Contains(cat.ParentId!))
                    errors.Add($"category '{cat.Id}': 'parentId' references unknown category '{cat.ParentId}'");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Taxonomy '{sourceLabel}' failed validation:{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", errors)}");
        }
    }

    // ---- JSON wire-format DTOs -------------------------------------------------------------
    // Separate from the public domain types so the on-disk schema can evolve (adding optional
    // fields, renaming) without forcing breaking changes on consumers of TaxonomyCategory.

    private sealed class TaxonomyDocument
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<CategoryDocument>? Categories { get; set; }
    }

    private sealed class CategoryDocument
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? ParentId { get; set; }
        public List<string>? FilenameKeywords { get; set; }
        public List<string>? FolderKeywords { get; set; }
        public List<string>? ContentKeywords { get; set; }
        public List<string>? ContentPhrases { get; set; }
        public List<string>? NegativeKeywords { get; set; }
        public List<string>? MimeTypes { get; set; }
        public double? BaseConfidence { get; set; }
    }
}
