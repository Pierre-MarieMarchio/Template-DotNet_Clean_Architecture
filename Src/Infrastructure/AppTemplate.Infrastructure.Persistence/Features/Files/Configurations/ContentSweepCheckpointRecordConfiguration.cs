using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Configurations;

internal sealed class ContentSweepCheckpointRecordConfiguration : IEntityTypeConfiguration<ContentSweepCheckpointRecord>
{
    internal const int MaxNameLength = 64;

    public void Configure(EntityTypeBuilder<ContentSweepCheckpointRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ContentSweepCheckpoints", AppDbContext.FilesSchema);

        builder.HasKey(checkpoint => checkpoint.Name);

        builder.Property(checkpoint => checkpoint.Name)
            .HasMaxLength(MaxNameLength)
            .ValueGeneratedNever();

        builder.Property(checkpoint => checkpoint.LastKey).HasMaxLength(ObjectKey.MaxLength);
        builder.Property(checkpoint => checkpoint.UpdatedAt).IsRequired();
    }
}
