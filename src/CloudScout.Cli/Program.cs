using CloudScout.Cli.Commands;
using CloudScout.Core.Crawling;
using CloudScout.Core.Persistence;
using CloudScout.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.CommandLine;

// ---- Host bootstrap -----------------------------------------------------------------
// Host.CreateApplicationBuilder gives us config + DI + logging out of the box. We layer
// Serilog on top for nicer console output, and an optional appsettings.Local.json for
// per-developer overrides (client id, tenant id) that must not be committed to git.

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // appsettings.json ships with the app (has empty placeholders); appsettings.Local.json is
    // gitignored and supplies real secrets for local development. Missing = fine.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.AddCloudScoutCrawling(builder.Configuration);
    builder.Services.AddCloudScoutServices(builder.Configuration);

    using var host = builder.Build();

    // Apply pending EF migrations so the SQLite DB is ready before the command runs.
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CloudScoutDbContext>();
        await db.Database.MigrateAsync();
    }

    var root = new RootCommand("CloudScout — pluggable document classifier for cloud storage")
    {
        ConnectCommand.Build(host.Services),
        ScanCommand.Build(host.Services),
        ResultsCommand.Build(host.Services),
    };

    return await root.Parse(args).InvokeAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Fatal error in CLI host bootstrap");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
