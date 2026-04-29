using CloudScout.Core.Taxonomy;

namespace CloudScout.Core.Classification;

/// <summary>
/// Scores files based on keyword and phrase hits in their extracted text body (populated by
/// Tier 1 extraction upstream). Uses word-boundary matching — substring matching on raw text
/// produces too many false positives for single-word keywords (e.g. "will" matching "william",
/// "statement" matching "misstatement"). Phrases are treated identically since the space in a
/// phrase already acts as a boundary.
///
/// Scoring model (V1):
///   - first content keyword hit contributes 0.40 weight, each additional hit +0.10 (cap at 1.0)
///   - first content phrase hit contributes 0.60 weight, each additional +0.15 (cap at 1.0)
///   - keyword and phrase contributions add; total is capped at 1.0 before applying baseConfidence
///   - any negative keyword in the extracted text zeroes the category
///
/// Returns empty when there is no extracted text — the pipeline falls back to whatever Tier 0 produced.
/// </summary>
public sealed class Tier1KeywordClassifier : IClassificationTier
{
    private const double FirstKeywordWeight = 0.40;
    private const double AdditionalKeywordWeight = 0.10;
    private const double FirstPhraseWeight = 0.60;
    private const double AdditionalPhraseWeight = 0.15;
    private const double MinReportableConfidence = 0.15;

    public int Tier => 1;

    public Task<IReadOnlyList<ClassificationResult>> ClassifyAsync(
        ClassificationContext context,
        TaxonomyDefinition taxonomy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(taxonomy);

        var text = context.ExtractedText;
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<IReadOnlyList<ClassificationResult>>(Array.Empty<ClassificationResult>());

        var results = new List<ClassificationResult>(capacity: taxonomy.Categories.Count);

        foreach (var category in taxonomy.Categories)
        {
            if (HasNegativeMatch(category.NegativeKeywords, text))
                continue;

            var keywordHits = CountWordMatches(category.ContentKeywords, text);
            var phraseHits = CountWordMatches(category.ContentPhrases, text);

            if (keywordHits == 0 && phraseHits == 0) continue;

            var keywordWeight = keywordHits > 0
                ? FirstKeywordWeight + (keywordHits - 1) * AdditionalKeywordWeight
                : 0;
            var phraseWeight = phraseHits > 0
                ? FirstPhraseWeight + (phraseHits - 1) * AdditionalPhraseWeight
                : 0;

            var totalWeight = Math.Min(1.0, keywordWeight + phraseWeight);
            var confidence = Math.Round(totalWeight * category.BaseConfidence, 3);
            if (confidence < MinReportableConfidence) continue;

            results.Add(new ClassificationResult(
                CategoryId: category.Id,
                ConfidenceScore: confidence,
                Tier: Tier,
                Reason: $"{keywordHits} keyword hit{(keywordHits == 1 ? "" : "s")}, {phraseHits} phrase hit{(phraseHits == 1 ? "" : "s")} in content"));
        }

        IReadOnlyList<ClassificationResult> ordered = results
            .OrderByDescending(r => r.ConfidenceScore)
            .ToList();
        return Task.FromResult(ordered);
    }

    private static int CountWordMatches(IReadOnlyList<string> needles, string haystack)
    {
        if (needles.Count == 0) return 0;

        var count = 0;
        foreach (var needle in needles)
        {
            if (string.IsNullOrWhiteSpace(needle)) continue;
            if (KeywordMatcher.ContainsWord(haystack, needle)) count++;
        }
        return count;
    }

    private static bool HasNegativeMatch(IReadOnlyList<string> negatives, string text)
    {
        foreach (var neg in negatives)
        {
            if (string.IsNullOrWhiteSpace(neg)) continue;
            if (KeywordMatcher.ContainsWord(text, neg)) return true;
        }
        return false;
    }
}
