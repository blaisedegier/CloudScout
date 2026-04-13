using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CloudScout.Core.Persistence;

public class CloudScoutDbContext : DbContext
{
    public CloudScoutDbContext(DbContextOptions<CloudScoutDbContext> options) : base(options) { }

    public DbSet<CloudConnection> CloudConnections => Set<CloudConnection>();
    public DbSet<ScanSession> ScanSessions => Set<ScanSession>();
    public DbSet<CrawledFile> CrawledFiles => Set<CrawledFile>();
    public DbSet<FileSuggestion> FileSuggestions => Set<FileSuggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CloudScoutDbContext).Assembly);
    }
}
