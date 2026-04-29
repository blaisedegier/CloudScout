using CloudScout.Core.Taxonomy;

namespace CloudScout.Core.Classification;

/// <summary>
/// Scores files using only their path and MIME type — no content download, no text extraction.
/// Cheap and runs in milliseconds, which makes it the first line of classification. A file is
/// scored against every category independently; categories that match nothing score zero and
/// are omitted from the result.
///
/// Scoring model (V1):
///   - filename keyword hit contributes 0.6 of the category's base confidence ceiling
///   - folder keyword hit contributes 0.4
///   - preferred MIME type match adds a flat +0.1 bonus
///   - any negative-keyword hit (in filename, folder, or parent path) zeroes the category
///   - the combined weight is capped at 1.0, then multiplied by the category's BaseConfidence
///
/// The model is deliberately simple so it's debuggable and testable. Tier 1 adds the more
/// nuanced content-based scoring.
/// </summary>
public sealed class Tier0MetadataClassifier : IClassificationTier
{
    private const double FilenameWeight = 0.6;
    private const double FolderWeight = 0.4;
    private const double MimeBonus = 0.1;
    private const double MinReportableConfidence = 0.1;

    public int Tier => 0;

    public Task<IReadOnlyList<ClassificationResult>> ClassifyAsync(
        ClassificationContext context,
        TaxonomyDefinition taxonomy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(taxonomy);

        var fileName = context.FileName ?? string.Empty;
        var folderPath = context.ParentFolderPath ?? string.Empty;
        var fullPath = context.FullPath ?? string.Empty;
        var mimeType = context.MimeType ?? string.Empty;

        var results = new List<ClassificationResult>(capacity: taxonomy.Categories.Count);

        foreach (var category in taxonomy.Categories)
        {
            // Any negative keyword anywhere in the path excludes the category outright.
            if (HasNegativeKeywordHit(category.NegativeKeywords, fileName, folderPath, fullPath))
                continue;

            var matchedFilenameKeywords = MatchingKeywords(category.FilenameKeywords, fileName);
            var matchedFolderKeywords = MatchingKeywords(category.FolderKeywords, folderPath);
            var mimeMatched = category.MimeTypes.Count > 0 &&
                              category.MimeTypes.Any(m => string.Equals(m, mimeType, StringComparison.OrdinalIgnoreCase));

            if (matchedFilenameKeywords.Count == 0 && matchedFolderKeywords.Count == 0 && !mimeMatched)
                continue;

            var signalWeight =
                (matchedFilenameKeywords.Count > 0 ? FilenameWeight : 0) +
                (matchedFolderKeywords.Count > 0 ? FolderWeight : 0) +
                (mimeMatched ? MimeBonus : 0);
            if (signalWeight > 1.0) signalWeight = 1.0;

            var confidence = Math.Round(signalWeight * category.BaseConfidence, 3);
            if (confidence < MinReportableConfidence) continue;

            results.Add(new ClassificationResult(
                CategoryId: category.Id,
                ConfidenceScore: confidence,
                Tier: Tier,
                Reason: BuildReason(matchedFilenameKeywords, matchedFolderKeywords, mimeMatched, mimeType)));
        }

        IReadOnlyList<ClassificationResult> ordered = results
            .OrderByDescending(r => r.ConfidenceScore)
            .ToList();
        return Task.FromResult(ordered);
    }

    private static List<string> MatchingKeywords(IReadOnlyList<string> keywords, string haystack)
    {
        if (keywords.Count == 0 || string.IsNullOrEmpty(haystack)) return new List<string>(0);

        var hits = new List<string>();
        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw)) continue;
            if (KeywordMatcher.ContainsWord(haystack, kw))
                hits.Add(kw);
        }
        return hits;
    }

    private static bool HasNegativeKeywordHit(IReadOnlyList<string> negatives, string fileName, string folderPath, string fullPath)
    {
        if (negatives.Count == 0) return false;
        foreach (var neg in negatives)
        {
            if (string.IsNullOrWhiteSpace(neg)) continue;
            if (KeywordMatcher.ContainsWord(fileName, neg)) return true;
            if (KeywordMatcher.ContainsWord(folderPath, neg)) return true;
            if (KeywordMatcher.ContainsWord(fullPath, neg)) return true;
        }
        return false;
    }

    private static string BuildReason(
        List<string> filenameHits,
        List<string> folderHits,
        bool mimeMatched,
        string mimeType)
    {
        var parts = new List<string>(3);
        if (filenameHits.Count > 0)
            parts.Add($"filename keyword {(filenameHits.Count == 1 ? "" : "s ")}{string.Join(", ", filenameHits.Select(h => $"'{h}'"))}");
        if (folderHits.Count > 0)
            parts.Add($"folder keyword {(folderHits.Count == 1 ? "" : "s ")}{string.Join(", ", folderHits.Select(h => $"'{h}'"))}");
        if (mimeMatched)
            parts.Add($"mime type {mimeType}");
        return string.Join("; ", parts);
    }
}
