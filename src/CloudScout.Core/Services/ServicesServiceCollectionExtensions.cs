using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using CloudScout.Core.Persistence;

namespace CloudScout.Core.Services;

public static class ServicesServiceCollectionExtensions
{
    public const string DatabaseSectionName = "Database";

    /// <summary>
    /// Registers the persistence context and top-level orchestration services. The SQLite
    /// connection string is read from the "Database:ConnectionString" config value, falling
    /// back to a sibling <c>cloudscout.db</c> file if not supplied.
    /// </summary>
    public static IServiceCollection AddCloudScoutServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[$"{DatabaseSectionName}:ConnectionString"]
            ?? "Data Source=cloudscout.db";

        services.AddDbContext<CloudScoutDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ScanOrchestrator>();

        return services;
    }
}
