using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudScout.Core.Persistence;

/// <summary>
/// Discovered by <c>dotnet ef</c> at design time so that migrations can be generated
/// directly from the Core project without needing to resolve a full host. The connection
/// string here is only used for schema generation — runtime configuration comes from
/// <see cref="Microsoft.Extensions.DependencyInjection"/> wiring.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CloudScoutDbContext>
{
    public CloudScoutDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CloudScoutDbContext>()
            .UseSqlite("Data Source=cloudscout.design-time.db")
            .Options;

        return new CloudScoutDbContext(options);
    }
}
