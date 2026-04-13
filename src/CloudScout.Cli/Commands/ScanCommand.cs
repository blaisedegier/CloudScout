using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using CloudScout.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;

namespace CloudScout.Cli.Commands;

/// <summary>
/// <c>cloudscout scan [--taxonomy NAME] [--connection-id GUID]</c> — runs a full crawl of
/// the user's cloud storage through the scan orchestrator, printing progress as it goes.
/// Defaults to the most recently connected account when no connection id is supplied.
/// </summary>
public static class ScanCommand
{
    public static Command Build(IServiceProvider services)
    {
        var taxonomyOption = new Option<string>("--taxonomy")
        {
            Description = "Taxonomy to classify against. Ignored in Phase A (crawl only).",
            DefaultValueFactory = _ => "generic-default",
        };

        var connectionIdOption = new Option<Guid?>("--connection-id")
        {
            Description = "Explicit CloudConnection id to scan. Defaults to the most recently used active connection.",
        };

        var command = new Command("scan", "Enumerate files from a connected cloud storage provider")
        {
            taxonomyOption,
            connectionIdOption,
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var taxonomy = parseResult.GetValue(taxonomyOption)!;
            var connectionId = parseResult.GetValue(connectionIdOption);
            return await RunAsync(services, taxonomy, connectionId, ct);
        });

        return command;
    }

    private static async Task<int> RunAsync(IServiceProvider services, string taxonomy, Guid? connectionId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudScoutDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ScanOrchestrator>();

        var connection = await ResolveConnectionAsync(db, connectionId, ct);
        if (connection is null)
        {
            Console.Error.WriteLine("No active cloud connection found. Run 'cloudscout connect onedrive' first.");
            return 2;
        }

        Console.WriteLine($"Scanning {connection.Provider} account {connection.AccountIdentifier}...");

        // Lightweight console progress — just overwrite a single line with the latest count.
        var lastRenderedAt = DateTime.UtcNow;
        var session = await orchestrator.RunScanAsync(
            connection.Id,
            taxonomy,
            onProgress: progress =>
            {
                // Throttle output to ~4 updates per second; the orchestrator emits once per batch.
                var now = DateTime.UtcNow;
                if ((now - lastRenderedAt).TotalMilliseconds < 250 && progress.Phase != ScanPhase.Completed) return Task.CompletedTask;
                lastRenderedAt = now;
                Console.Write($"\r  {progress.Phase}: {progress.FilesDiscovered} files discovered  ");
                if (progress.Phase is ScanPhase.Completed or ScanPhase.Failed) Console.WriteLine();
                return Task.CompletedTask;
            },
            cancellationToken: ct);

        Console.WriteLine();
        Console.WriteLine($"Scan {session.Id} {session.Status.ToLowerInvariant()}: {session.TotalFilesFound} files.");
        return session.Status == ScanStatus.Completed ? 0 : 1;
    }

    private static async Task<CloudConnection?> ResolveConnectionAsync(CloudScoutDbContext db, Guid? connectionId, CancellationToken ct)
    {
        if (connectionId is Guid id)
            return await db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id && c.Status == ConnectionStatus.Active, ct);

        return await db.CloudConnections
            .Where(c => c.Status == ConnectionStatus.Active)
            .OrderByDescending(c => c.LastUsedUtc ?? c.ConnectedUtc)
            .FirstOrDefaultAsync(ct);
    }
}
