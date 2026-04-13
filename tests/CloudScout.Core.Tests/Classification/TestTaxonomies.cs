using CloudScout.Core.Taxonomy;

namespace CloudScout.Core.Tests.Classification;

/// <summary>
/// Hand-built taxonomies for tests. Small and deterministic — we don't want rule-change
/// pressure on the real generic-default taxonomy to cascade into test churn. Each fixture
/// exposes a scenario just rich enough to exercise one classifier behaviour.
/// </summary>
internal static class TestTaxonomies
{
    /// <summary>
    /// Three categories with a mix of filename, folder, content, and negative signals.
    /// Use this as the default for most tests.
    /// </summary>
    public static TaxonomyDefinition Mixed() => new(
        name: "Test Mixed",
        version: "1.0",
        categories: new List<TaxonomyCategory>
        {
            new()
            {
                Id = "wills",
                DisplayName = "Wills",
                FilenameKeywords = new[] { "will", "testament" },
                FolderKeywords = new[] { "estate" },
                ContentKeywords = new[] { "testator", "executor" },
                ContentPhrases = new[] { "last will and testament" },
                NegativeKeywords = new[] { "goodwill" },
                MimeTypes = new[] { "application/pdf" },
                BaseConfidence = 0.9,
            },
            new()
            {
                Id = "banking",
                DisplayName = "Banking",
                FilenameKeywords = new[] { "statement" },
                FolderKeywords = new[] { "bank" },
                ContentKeywords = new[] { "iban", "account number" },
                ContentPhrases = new[] { "closing balance" },
                NegativeKeywords = new[] { "personal statement" },
                BaseConfidence = 0.8,
            },
            new()
            {
                Id = "receipts",
                DisplayName = "Receipts",
                FilenameKeywords = new[] { "invoice", "receipt" },
                ContentKeywords = new[] { "subtotal", "total due" },
                BaseConfidence = 0.7,
            },
        });
}
