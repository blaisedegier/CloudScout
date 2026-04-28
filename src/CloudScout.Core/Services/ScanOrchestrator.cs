using CloudScout.Core.Classification;
using CloudScout.Core.Classification.Extraction;
using CloudScout.Core.Crawling;
using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using CloudScout.Core.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudScout.Core.Services;

/// <summary>
/// Coordinates a full scan run: resolves the connection, drives the crawler, classifies each
/// discovered file, and keeps the owning <see cref="ScanSession"/> row up to date so an
/// interrupted scan can be resumed.
///
/// The scan runs in two phases:
///   1. <b>Crawl</b> — enumerate every file via <see cref="ICloudStorageProvider"/> and persist
///      metadata-only <see cref="CrawledFile"/> rows in batches. Cheap; proceeds without
///      touching file content.
///   2. <b>Classify</b> — for each crawled file, run Tier 0 (metadata heuristics). If Tier 0
///      is below the high-confidence threshold *and* a text extractor exists for the file type,
///      download the content, extract text, and run Tier 1. Persist the top-N
///      <see cref="FileSuggestion"/> candidates. Tier 3 (LLM) is not wired up in Phase B.
/// </summary>
public sealed class ScanOrchestrator
{
    // Number of CrawledFile rows accumulated before flushing — bounded memory on large drives.
    private const int CrawlPersistBatchSize = 50;

    // FileSuggestion rows accumulate faster (up to 3 per file). Flush more often so the DB
    // reflects progress for a UI watching the session rows.
    private const int ClassifyPersistEveryNFiles = 10;

    // Tier 0 confidence above which we skip Tier 1. At 0.7 a single strong filename+folder
    // match (e.g. /Car/COR.pdf → vehicle.registration) short-circuits without a download.
    private const double HighConfidenceThreshold = 0.7;

    // Keep only the top N suggestions per file. More than 3 is noise; fewer drops nuance.
    private const int MaxSuggestionsPerFile = 3;

    private readonly CloudScoutDbContext _db;
    private readonly IEnumerable<ICloudStorageProvider> _providers;
    private readonly ClassificationPipeline _pipeline;
    private readonly TextExtractionService _textExtraction;
    private readonly ITaxonomyProvider _taxonomyProvider;
    private readonly ILogger<ScanOrchestrator> _logger;

    public ScanOrchestrator(
        CloudScoutDbContext db,
        IEnumerable<ICloudStorageProvider> providers,
        ClassificationPipeline pipeline,
        TextExtractionService textExtraction,
        ITaxonomyProvider taxonomyProvider,
        ILogger<ScanOrchestrator> logger)
    {
        _db = db;
        _providers = providers;
        _pipeline = pipeline;
        _textExtraction = textExtraction;
        _taxonomyProvider = taxonomyProvider;
        _logger = logger;
    }

    public async Task<ScanSession> RunScanAsync(
        Guid connectionId,
        string taxonomyName,
        Func<ScanProgress, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken)
            ?? throw new InvalidOperationException($"No cloud connection with id {connectionId}.");

        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, connection.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No registered ICloudStorageProvider for '{connection.Provider}'.");

        // Fail fast if the taxonomy doesn't resolve — no point crawling otherwise.
        var taxonomy = await _taxonomyProvider.GetAsync(taxonomyName, cancellationToken);

        var session = await GetOrCreateSessionAsync(connection, taxonomyName, cancellationToken);

        try
        {
            await CrawlAndPersistAsync(provider, session, connection, onProgress, cancellationToken);
            await ClassifyCrawledFilesAsync(provider, session, connection, taxonomy, onProgress, cancellationToken);

            session.Status = ScanStatus.Completed;
            session.CompletedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await ReportProgressAsync(onProgress, session, currentPath: null, ScanPhase.Completed);

            _logger.LogInformation("Scan {SessionId} completed: {FileCount} files, {ClassifiedCount} classified",
                session.Id, session.TotalFilesFound, session.ClassifiedCount);
        }
        catch (OperationCanceledException)
        {
            session.Status = ScanStatus.Cancelled;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Scan {SessionId} cancelled at path {LastPath}", session.Id, session.LastProcessedExternalPath);
            throw;
        }
        catch (Exception ex)
        {
            session.Status = ScanStatus.Failed;
            session.ErrorMessage = ex.Message;
            session.CompletedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await ReportProgressAsync(onProgress, session, currentPath: null, ScanPhase.Failed);
            _logger.LogError(ex, "Scan {SessionId} failed", session.Id);
            throw;
        }

        return session;
    }

    // ---- Phase 1: Crawl ----------------------------------------------------------------------

    private async Task<ScanSession> GetOrCreateSessionAsync(
        CloudConnection connection,
        string taxonomyName,
        CancellationToken ct)
    {
        var existing = await _db.ScanSessions
            .Where(s => s.ConnectionId == connection.Id && s.Status == ScanStatus.Running)
            .OrderByDescending(s => s.StartedUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            _logger.LogInformation("Resuming scan {SessionId} from path {LastPath}", existing.Id, existing.LastProcessedExternalPath);
            return existing;
        }

        var session = new ScanSession
        {
            ConnectionId = connection.Id,
            TaxonomyName = taxonomyName,
            Status = ScanStatus.Running,
            StartedUtc = DateTime.UtcNow,
        };
        _db.ScanSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    private async Task CrawlAndPersistAsync(
        ICloudStorageProvider provider,
        ScanSession session,
        CloudConnection connection,
        Func<ScanProgress, Task>? onProgress,
        CancellationToken ct)
    {
        var batch = new List<CrawledFile>(CrawlPersistBatchSize);

        await foreach (var file in provider.EnumerateFilesAsync(connection.HomeAccountId, session.LastProcessedExternalPath, ct)
                                           .ConfigureAwait(false))
        {
            batch.Add(new CrawledFile
            {
                SessionId = session.Id,
                ExternalFileId = file.ExternalFileId,
                ExternalPath = file.ExternalPath,
                FileName = file.FileName,
                ParentFolderPath = file.ParentFolderPath,
                MimeType = file.MimeType,
                SizeBytes = file.SizeBytes,
                CreatedUtc = file.CreatedUtc,
                ModifiedUtc = file.ModifiedUtc,
            });

            session.TotalFilesFound++;
            session.LastProcessedExternalPath = file.ExternalPath;

            if (batch.Count >= CrawlPersistBatchSize)
            {
                await FlushBatchAsync(batch, ct);
                await ReportProgressAsync(onProgress, session, file.ExternalPath, ScanPhase.Crawling);
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, ct);
            await ReportProgressAsync(onProgress, session, session.LastProcessedExternalPath, ScanPhase.Crawling);
        }

        connection.LastUsedUtc = DateTime.UtcNow;
    }

    private async Task FlushBatchAsync(List<CrawledFile> batch, CancellationToken ct)
    {
        _db.CrawledFiles.AddRange(batch);
        await _db.SaveChangesAsync(ct);
        batch.Clear();
    }

    // ---- Phase 2: Classify -------------------------------------------------------------------

    private async Task ClassifyCrawledFilesAsync(
        ICloudStorageProvider provider,
        ScanSession session,
        CloudConnection connection,
        TaxonomyDefinition taxonomy,
        Func<ScanProgress, Task>? onProgress,
        CancellationToken ct)
    {
        // Files in this session that haven't yet produced any suggestions. Using the relation
        // (Suggestions navigation) keeps the query correct across resumed runs — previously
        // classified files are skipped automatically.
        var filesToClassify = await _db.CrawledFiles
            .Where(f => f.SessionId == session.Id && !f.Suggestions.Any())
            .OrderBy(f => f.ExternalPath)
            .ToListAsync(ct);

        if (filesToClassify.Count == 0) return;

        _logger.LogInformation("Classifying {Count} files for scan {SessionId}", filesToClassify.Count, session.Id);

        var processedSinceFlush = 0;
        foreach (var file in filesToClassify)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await ClassifyFileAsync(provider, connection, file, taxonomy, ct);

            foreach (var candidate in candidates.OrderByDescending(c => c.ConfidenceScore).Take(MaxSuggestionsPerFile))
            {
                _db.FileSuggestions.Add(new FileSuggestion
                {
                    FileId = file.Id,
                    SuggestedCategoryId = candidate.CategoryId,
                    ConfidenceScore = candidate.ConfidenceScore,
                    ClassificationTier = candidate.Tier,
                    ClassificationReason = candidate.Reason,
                });
            }

            session.ClassifiedCount++;
            processedSinceFlush++;

            if (processedSinceFlush >= ClassifyPersistEveryNFiles)
            {
                await _db.SaveChangesAsync(ct);
                processedSinceFlush = 0;
                await ReportProgressAsync(onProgress, session, file.ExternalPath, ScanPhase.Classifying);
            }
        }

        if (processedSinceFlush > 0)
            await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<ClassificationResult>> ClassifyFileAsync(
        ICloudStorageProvider provider,
        CloudConnection connection,
        CrawledFile file,
        TaxonomyDefinition taxonomy,
        CancellationToken ct)
    {
        var context = new ClassificationContext
        {
            FileName = file.FileName,
            ParentFolderPath = file.ParentFolderPath,
            FullPath = file.ExternalPath,
            MimeType = file.MimeType,
            SizeBytes = file.SizeBytes,
        };

        // Tier 0 is cheap — always run it.
        var tier0Results = await _pipeline.RunTierAsync(0, context, taxonomy, ct);
        var tier0Max = tier0Results.FirstOrDefault()?.ConfidenceScore ?? 0;

        var allResults = new List<ClassificationResult>(tier0Results);

        // Decide whether to download the file's content. Three reasons we'd want to:
        //   1. There's a text extractor → run Tier 1
        //   2. It's an image → load bytes for Tier 3 vision
        //   3. Both — text extractors win, since text classification is cheaper and more reliable
        // None of these matter if Tier 0 already reached high confidence.
        var canExtractText = _textExtraction.HasExtractorFor(file.MimeType, file.FileName);
        var isImage = IsImageMimeType(file.MimeType);
        var worthDownloading = tier0Max < HighConfidenceThreshold && (canExtractText || isImage);
        if (!worthDownloading) return allResults;

        try
        {
            await using var stream = await provider.DownloadAsync(connection.HomeAccountId, file.ExternalFileId, ct);

            if (canExtractText)
            {
                var text = await _textExtraction.ExtractAsync(stream, file.MimeType, file.FileName, cancellationToken: ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    context.ExtractedText = text;
                    var tier1Results = await _pipeline.RunTierAsync(1, context, taxonomy, ct);
                    allResults.AddRange(tier1Results);
                }
            }
            else if (isImage)
            {
                // Buffer the image into memory so Tier 3 can pass it to the multimodal model.
                // Bounded by the per-file size (orchestrator only downloads bytes when needed)
                // so total memory stays proportional to one file at a time.
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                context.SourceBytes = buffer.ToArray();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Content fetch failed for {Path}; using Tier 0 result only", file.ExternalPath);
        }

        // Tier 3 (LLM) — most expensive. Only invoke when T0+T1 combined didn't reach confidence
        // threshold. Tier 3 handles IsAvailable internally: if no model is configured it returns
        // empty, costing nothing. The LLM sees whatever metadata + extracted text is in the
        // context — it doesn't need to re-download the file.
        var combinedMax = allResults.Max(r => (double?)r.ConfidenceScore) ?? 0;
        if (combinedMax < HighConfidenceThreshold)
        {
            try
            {
                var tier3Results = await _pipeline.RunTierAsync(3, context, taxonomy, ct);
                allResults.AddRange(tier3Results);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tier 3 LLM inference failed for {Path}; continuing with Tier 0/1 results", file.ExternalPath);
            }
        }

        return allResults;
    }

    /// <summary>
    /// Conservative image-MIME predicate matching the formats the multimodal projector handles.
    /// Mirrors <c>Tier3LlmClassifier.ShouldSendImage</c> — keeping the orchestrator's "should I
    /// download" decision aligned with the classifier's "should I send to the model" decision
    /// avoids buffering bytes the classifier won't use.
    /// </summary>
    private static bool IsImageMimeType(string? mimeType) =>
        mimeType is not null && (
            mimeType.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("image/png", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("image/webp", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("image/gif", StringComparison.OrdinalIgnoreCase));

    // ---- Progress reporting ------------------------------------------------------------------

    private static Task ReportProgressAsync(
        Func<ScanProgress, Task>? onProgress,
        ScanSession session,
        string? currentPath,
        string phase)
    {
        if (onProgress is null) return Task.CompletedTask;
        var progress = new ScanProgress(session.Id, session.TotalFilesFound, session.ClassifiedCount, currentPath, phase);
        return onProgress(progress);
    }
}
