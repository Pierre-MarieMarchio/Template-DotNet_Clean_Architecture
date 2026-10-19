using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Configurations;

/// <summary>
/// Mapping for a tag row. The key is the pair, not a surrogate, so a duplicate tag on one file is
/// impossible in the database.
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
