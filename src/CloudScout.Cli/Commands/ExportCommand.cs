using CloudScout.Core.Export;
using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using CloudScout.Core.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace CloudScout.Cli.Commands;

/// <summary>
/// <c>cloudscout export &lt;path&gt; [--session-id GUID]</c> — writes scan results to JSON or CSV.
/// Format is inferred from the file extension. Defaults to the most recent completed session.
/// </summary>
public static class ExportCommand
{
    public static Command Build(IServiceProvider services)
    {
        var pathArg = new Argument<string>("path")
        {
            Description = "Destination file path. Use a .json or .csv extension to select format.",
        };

        var sessionIdOption = new Option<Guid?>("--session-id")
        {
            Description = "Scan session to export. Defaults to the most recent completed or cancelled session.",
        };

        var command = new Command("export", "Export scan results to a JSON or CSV file")
        {
            pathArg,
            sessionIdOption,
        };

        command.SetAction(CommandErrorHandler.Wrap(async (parseResult, ct) =>
        {
            var path = parseResult.GetValue(pathArg)!;
            var sessionId = parseResult.GetValue(sessionIdOption);
            return await RunAsync(services, path, sessionId, ct);
        }));

        return command;
    }

    private static async Task<int> RunAsync(IServiceProvider services, string path, Guid? sessionId, CancellationToken ct)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".json" and not ".csv")
        {
            Console.Error.WriteLine("Unsupported export extension. Use .json or .csv.");
            return 2;
        }

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

        TaxonomyDefinition? taxonomy = null;
        try
        {
            taxonomy = await taxonomyProvider.GetAsync(session.TaxonomyName, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"(warning: could not load taxonomy '{session.TaxonomyName}' — display names omitted. {ex.Message})");
        }

        var raw = await (
            from f in db.CrawledFiles.Where(f => f.SessionId == session.Id)
            let top = f.Suggestions.OrderByDescending(s => s.ConfidenceScore).FirstOrDefault()
            orderby f.ExternalPath
            select new
            {
                f.ExternalPath,
                f.SizeBytes,
                CategoryId = top != null ? top.SuggestedCategoryId : null,
                Confidence = top != null ? top.ConfidenceScore : (double?)null,
                Tier = top != null ? top.ClassificationTier : (int?)null,
                Reason = top != null ? top.ClassificationReason : null,
            })
            .ToListAsync(ct);

        var rows = raw.Select(r => new ExportRow(
            r.ExternalPath,
            r.SizeBytes,
            r.CategoryId,
            r.CategoryId is null ? null : (taxonomy?.GetById(r.CategoryId)?.DisplayName ?? r.CategoryId),
            r.Confidence,
            r.Tier,
            r.Reason)).ToList();

        // Atomic write: stage to a sibling temp file and Move into place. A Ctrl-C mid-write
        // leaves the temp behind but doesn't clobber an existing export.
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tempPath = fullPath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            if (ext == ".json")
            {
                await ExportFormatter.WriteJsonAsync(rows, stream, ct);
            }
            else
            {
                await using var writer = new StreamWriter(stream);
                ExportFormatter.WriteCsv(rows, writer);
            }
        }
        File.Move(tempPath, fullPath, overwrite: true);

        Console.WriteLine($"Exported {rows.Count} files to {fullPath}");
        return 0;
    }
}
