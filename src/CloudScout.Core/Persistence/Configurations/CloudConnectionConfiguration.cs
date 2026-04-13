using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudScout.Core.Persistence.Configurations;

internal sealed class CloudConnectionConfiguration : IEntityTypeConfiguration<CloudConnection>
{
    public void Configure(EntityTypeBuilder<CloudConnection> builder)
    {
        builder.ToTable("CloudConnections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AccountIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(x => x.HomeAccountId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(x => new { x.Provider, x.AccountIdentifier }).IsUnique();
        builder.HasIndex(x => x.HomeAccountId);
        builder.HasIndex(x => x.Status);
    }
}
