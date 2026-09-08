using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Configurations;

/// <summary>
/// Mapping for a tag row. The key is the pair, not a surrogate: a tag has no identity of its own, so
/// the thing that identifies the row is the owner plus the value — and that makes a duplicate tag on
/// one file impossible in the database rather than merely unlikely.
/// </summary>
internal sealed class StoredFileTagRecordConfiguration : IEntityTypeConfiguration<StoredFileTagRecord>
{
    public void Configure(EntityTypeBuilder<StoredFileTagRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StoredFileTags", AppDbContext.FilesSchema);

        builder.HasKey(tag => new { tag.StoredFileId, tag.Value });

        builder.Property(tag => tag.Value)
            .HasMaxLength(Tag.MaxLength)
            .IsRequired();
    }
}
