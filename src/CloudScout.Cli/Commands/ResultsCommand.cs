using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace CloudScout.Cli.Commands;

/// <summary>
/// <c>cloudscout results [--session-id GUID] [--limit N]</c> — prints the files captured
/// by a scan session, grouped by parent folder. Defaults to the most recent completed session.
/// Phase A shows raw discoveries; later phases will group by suggested category instead.
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
            Description = "Maximum number of files to print per folder.",
            DefaultValueFactory = _ => 20,
        };

        var command = new Command("results", "Show files captured by a completed scan")
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

        Console.WriteLine($"Session {session.Id} — {session.Status}");
        Console.WriteLine($"Started:   {session.StartedUtc:u}");
        if (session.CompletedUtc is { } done) Console.WriteLine($"Completed: {done:u}");
        Console.WriteLine($"Files:     {session.TotalFilesFound}");
        Console.WriteLine();

        var folders = await db.CrawledFiles
            .Where(f => f.SessionId == session.Id)
            .OrderBy(f => f.ParentFolderPath).ThenBy(f => f.FileName)
            .Select(f => new { f.ParentFolderPath, f.FileName, f.SizeBytes })
            .ToListAsync(ct);

        foreach (var folderGroup in folders.GroupBy(f => f.ParentFolderPath))
        {
            Console.WriteLine($"  {folderGroup.Key}/");
            foreach (var file in folderGroup.Take(limit))
                Console.WriteLine($"    {file.FileName}  ({FormatSize(file.SizeBytes)})");
            var overflow = folderGroup.Count() - limit;
            if (overflow > 0) Console.WriteLine($"    ... and {overflow} more");
        }

        return 0;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
