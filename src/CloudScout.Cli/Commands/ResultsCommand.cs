using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using CloudScout.Core.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace CloudScout.Cli.Commands;

/// <summary>
/// <c>cloudscout results [--session-id GUID] [--limit N]</c> — prints files captured by a scan
/// session, grouped by their top suggested category (unclassified files land in their own group).
/// Defaults to the most recently completed session.
/// </summary>
public static class ResultsCommand
{
    public static Command Build(IServiceProvider services)
    {
        var sessionIdOption = new Option<Guid?>("--session-id")
        {
            Description = "Scan session to display. Defaults to the most recent completed or cancelled session.",
        };

        var limitOption = new Option<int>("--limit")
        {
            Description = "Maximum number of files to print per category.",
            DefaultValueFactory = _ => 20,
        };

        var command = new Command("results", "Show files captured by a completed scan, grouped by suggested category")
        {
            sessionIdOption,
            limitOption,
        };

        command.SetAction(CommandErrorHandler.Wrap(async (parseResult, ct) =>
        {
            var sessionId = parseResult.GetValue(sessionIdOption);
            var limit = parseResult.GetValue(limitOption);
            return await RunAsync(services, sessionId, limit, ct);
        }));

        return command;
    }

    private static async Task<int> RunAsync(IServiceProvider services, Guid? sessionId, int limit, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudScoutDbContext>();
        var taxonomyProvider = scope.ServiceProvider.GetRequiredService<ITaxonomyProvider>();

        var session = sessionId is Guid id
            ? await db.ScanSessions.FirstOrDefaultAsync(s => s.Id == id, ct)
            : await db.ScanSessions
                .Where(s => s.Status != ScanStatus.Running)
                .OrderByDescending(s => s.StartedUtc)
                .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            Console.Error.WriteLine("No scan session found. Run 'cloudscout scan' first.");
            return 2;
        }

        // Aggregate ChangeStatus counts so the user can see at a glance how much of this scan
        // was reused from the prior session — high "Unchanged" means delta is doing its job.
        var statusCounts = await db.CrawledFiles
            .Where(f => f.SessionId == session.Id)
            .GroupBy(f => f.ChangeStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var newCount = statusCounts.GetValueOrDefault(ChangeStatusValues.New);
        var modifiedCount = statusCounts.GetValueOrDefault(ChangeStatusValues.Modified);
        var unchangedCount = statusCounts.GetValueOrDefault(ChangeStatusValues.Unchanged);

        Console.WriteLine($"Session {session.Id} — {session.Status}");
        Console.WriteLine($"Taxonomy:   {session.TaxonomyName}");
        Console.WriteLine($"Started:    {session.StartedUtc:u}");
        if (session.CompletedUtc is { } done) Console.WriteLine($"Completed:  {done:u}");
        Console.WriteLine($"Files:      {session.TotalFilesFound}");
        Console.WriteLine($"Classified: {session.ClassifiedCount}");
        Console.WriteLine($"Changes:    New: {newCount} · Modified: {modifiedCount} · Unchanged: {unchangedCount}");
        Console.WriteLine();

        // Resolve taxonomy so we can render friendly category names instead of raw ids.
        // If the taxonomy used during the scan is no longer available (e.g. a custom one at a
        // path that's since been deleted), fall back to the raw ids and keep going.
        TaxonomyDefinition? taxonomy = null;
        try
        {
            taxonomy = await taxonomyProvider.GetAsync(session.TaxonomyName, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"(warning: could not load taxonomy '{session.TaxonomyName}' — showing raw category ids. {ex.Message})");
        }

        // One row per file with the single highest-confidence suggestion (or null if none).
        // Projected server-side so we don't materialise entities we won't display.
        var rows = await (
            from f in db.CrawledFiles.Where(f => f.SessionId == session.Id)
            let top = f.Suggestions.OrderByDescending(s => s.ConfidenceScore).FirstOrDefault()
            orderby f.ExternalPath
            select new FileRow(
                f.ExternalPath,
                f.SizeBytes,
                top != null ? top.SuggestedCategoryId : null,
                top != null ? top.ConfidenceScore : (double?)null,
                top != null ? top.ClassificationTier : (int?)null,
                top != null ? top.ClassificationReason : null))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            Console.WriteLine("(no files in this session)");
            return 0;
        }

        // Group by category; unclassified (null) bucket sorts last.
        var grouped = rows
            .GroupBy(r => r.CategoryId)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var displayName = ResolveCategoryName(taxonomy, group.Key);
            Console.WriteLine($"  {displayName}  ({group.Count()} file{(group.Count() == 1 ? "" : "s")})");

            var ordered = group
                .OrderByDescending(r => r.Confidence ?? -1)
                .ThenBy(r => r.ExternalPath)
                .ToList();

            foreach (var row in ordered.Take(limit))
            {
                var confidence = row.Confidence is double c ? $"{c:P0}".PadLeft(4) : "  — ";
                var tier = row.Tier is int t ? $"T{t}" : "  ";
                var reason = string.IsNullOrWhiteSpace(row.Reason) ? "" : $"  [{row.Reason}]";
                Console.WriteLine($"    {confidence} {tier}  {row.ExternalPath}  ({FormatSize(row.SizeBytes)}){reason}");
            }

            var overflow = group.Count() - limit;
            if (overflow > 0) Console.WriteLine($"    ... and {overflow} more");
            Console.WriteLine();
        }

        return 0;
    }

    private static string ResolveCategoryName(TaxonomyDefinition? taxonomy, string? categoryId)
    {
        if (categoryId is null) return "Unclassified";
        if (taxonomy is null) return categoryId;
        return taxonomy.GetById(categoryId)?.DisplayName ?? categoryId;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private sealed record FileRow(string ExternalPath, long SizeBytes, string? CategoryId, double? Confidence, int? Tier, string? Reason);
}
