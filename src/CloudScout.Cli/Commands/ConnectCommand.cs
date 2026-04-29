using CloudScout.Core.Crawling;
using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace CloudScout.Cli.Commands;

/// <summary>
/// <c>cloudscout connect &lt;provider&gt;</c> — launches the interactive auth flow for the
/// specified provider and persists a <see cref="CloudConnection"/> row pointing at the
/// MSAL-cached account. Subsequent <c>scan</c> runs reuse this row without prompting.
/// </summary>
public static class ConnectCommand
{
    public static Command Build(IServiceProvider services)
    {
        var providerArg = new Argument<string>("provider")
        {
            Description = "Cloud provider to connect (supported: onedrive, googledrive, dropbox)",
        };

        var command = new Command("connect", "Connect a cloud storage provider")
        {
            providerArg,
        };

        command.SetAction(CommandErrorHandler.Wrap(async (parseResult, ct) =>
        {
            var providerName = parseResult.GetValue(providerArg)!;
            return await RunAsync(services, providerName, ct);
        }));

        return command;
    }

    private static async Task<int> RunAsync(IServiceProvider services, string providerName, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("connect");

        var provider = services.GetServices<ICloudStorageProvider>()
            .FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            Console.Error.WriteLine($"Unknown provider '{providerName}'. Supported: onedrive, googledrive, dropbox");
            return 2;
        }

        Console.WriteLine($"Starting device-code flow for {provider.ProviderName}...");

        var connectionResult = await provider.AuthenticateInteractiveAsync(
            deviceCodePrompt: msg =>
            {
                // MSAL's message already includes the URL and code; print it verbatim.
                Console.WriteLine();
                Console.WriteLine(msg);
                Console.WriteLine();
                return Task.CompletedTask;
            },
            cancellationToken: ct);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudScoutDbContext>();

        // A given (provider, accountIdentifier) should only produce one row — if the user reconnects
        // the same account, update the existing record rather than creating a duplicate.
        var existing = await db.CloudConnections
            .FirstOrDefaultAsync(c => c.Provider == provider.ProviderName && c.AccountIdentifier == connectionResult.AccountIdentifier, ct);

        if (existing is not null)
        {
            existing.HomeAccountId = connectionResult.HomeAccountId;
            existing.Status = ConnectionStatus.Active;
            existing.LastUsedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            Console.WriteLine($"Refreshed existing connection for {connectionResult.AccountIdentifier} (id: {existing.Id}).");
            return 0;
        }

        var connection = new CloudConnection
        {
            Provider = provider.ProviderName,
            AccountIdentifier = connectionResult.AccountIdentifier,
            HomeAccountId = connectionResult.HomeAccountId,
            Status = ConnectionStatus.Active,
        };
        db.CloudConnections.Add(connection);
        await db.SaveChangesAsync(ct);

        Console.WriteLine($"Connected {connectionResult.AccountIdentifier} (id: {connection.Id}).");
        logger.LogInformation("Stored new CloudConnection {Id} for provider {Provider}", connection.Id, provider.ProviderName);
        return 0;
    }
}
