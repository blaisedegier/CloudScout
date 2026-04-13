using CloudScout.Core.Crawling;
using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudScout.Core.Services;

/// <summary>
/// Coordinates a full scan run: resolves the connection, drives the crawler, persists
/// <see cref="CrawledFile"/> rows in batches, and keeps the owning <see cref="ScanSession"/>
/// row up to date so the scan can be resumed after an interruption.
///
/// Classification is intentionally out of scope here for Phase A — the orchestrator produces
/// the raw file set that the classification pipeline will later consume.
/// </summary>
public sealed class ScanOrchestrator
{
    // Number of CrawledFile rows accumulated before flushing to the DB. Chosen to keep memory
    // bounded on large drives while minimising round-trips; adjust if SQLite becomes a bottleneck.
    private const int PersistBatchSize = 50;

    private readonly CloudScoutDbContext _db;
    private readonly IEnumerable<ICloudStorageProvider> _providers;
    private readonly ILogger<ScanOrchestrator> _logger;

    public ScanOrchestrator(
        CloudScoutDbContext db,
        IEnumerable<ICloudStorageProvider> providers,
        ILogger<ScanOrchestrator> logger)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Runs a complete scan for <paramref name="connectionId"/>. If an incomplete session for that
    /// connection exists, it is resumed; otherwise a new session is created.
    /// </summary>
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

        var session = await GetOrCreateSessionAsync(connection, taxonomyName, cancellationToken);

        try
        {
            await CrawlAndPersistAsync(provider, session, connection, onProgress, cancellationToken);

            session.Status = ScanStatus.Completed;
            session.CompletedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await ReportProgressAsync(onProgress, session, currentPath: null, ScanPhase.Completed, cancellationToken);

            _logger.LogInformation("Scan {SessionId} completed: {FileCount} files discovered",
                session.Id, session.TotalFilesFound);
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
            await ReportProgressAsync(onProgress, session, currentPath: null, ScanPhase.Failed, CancellationToken.None);
            _logger.LogError(ex, "Scan {SessionId} failed", session.Id);
            throw;
        }

        return session;
    }

    private async Task<ScanSession> GetOrCreateSessionAsync(
        CloudConnection connection,
        string taxonomyName,
        CancellationToken ct)
    {
        // Resume a Running session for this connection if one exists, otherwise create fresh.
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
        var batch = new List<CrawledFile>(PersistBatchSize);

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

            if (batch.Count >= PersistBatchSize)
            {
                await FlushBatchAsync(batch, ct);
                await ReportProgressAsync(onProgress, session, file.ExternalPath, ScanPhase.Crawling, ct);
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, ct);
            await ReportProgressAsync(onProgress, session, session.LastProcessedExternalPath, ScanPhase.Crawling, ct);
        }

        connection.LastUsedUtc = DateTime.UtcNow;
    }

    private async Task FlushBatchAsync(List<CrawledFile> batch, CancellationToken ct)
    {
        _db.CrawledFiles.AddRange(batch);
        await _db.SaveChangesAsync(ct);
        batch.Clear();
    }

    private static Task ReportProgressAsync(
        Func<ScanProgress, Task>? onProgress,
        ScanSession session,
        string? currentPath,
        string phase,
        CancellationToken ct)
    {
        if (onProgress is null) return Task.CompletedTask;
        var progress = new ScanProgress(session.Id, session.TotalFilesFound, session.ClassifiedCount, currentPath, phase);
        return onProgress(progress);
    }
}
