using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudScout.Core.Persistence.Configurations;

internal sealed class ScanSessionConfiguration : IEntityTypeConfiguration<ScanSession>
{
    public void Configure(EntityTypeBuilder<ScanSession> builder)
    {
        builder.ToTable("ScanSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TaxonomyName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LastProcessedExternalPath).HasMaxLength(2000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasOne(x => x.Connection)
            .WithMany(c => c.Sessions)
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.StartedUtc);
    }
}
