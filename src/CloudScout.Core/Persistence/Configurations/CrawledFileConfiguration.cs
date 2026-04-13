using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudScout.Core.Persistence.Configurations;

internal sealed class CrawledFileConfiguration : IEntityTypeConfiguration<CrawledFile>
{
    public void Configure(EntityTypeBuilder<CrawledFile> builder)
    {
        builder.ToTable("CrawledFiles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalFileId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ExternalPath).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ParentFolderPath).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(200);

        builder.HasOne(x => x.Session)
            .WithMany(s => s.Files)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per (session, external file id) — a single scan should not enumerate the same file twice
        builder.HasIndex(x => new { x.SessionId, x.ExternalFileId }).IsUnique();
        builder.HasIndex(x => x.SessionId);
    }
}
