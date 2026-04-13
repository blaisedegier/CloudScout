using CloudScout.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudScout.Core.Persistence.Configurations;

internal sealed class FileSuggestionConfiguration : IEntityTypeConfiguration<FileSuggestion>
{
    public void Configure(EntityTypeBuilder<FileSuggestion> builder)
    {
        builder.ToTable("FileSuggestions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SuggestedCategoryId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ClassificationReason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.UserStatus).HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.File)
            .WithMany(f => f.Suggestions)
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.FileId);
        builder.HasIndex(x => new { x.FileId, x.ClassificationTier });
        builder.HasIndex(x => x.UserStatus);
    }
}
