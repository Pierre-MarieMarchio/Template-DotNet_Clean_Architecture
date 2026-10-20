using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Configurations;

internal sealed class StoredFileRecordConfiguration : IEntityTypeConfiguration<StoredFileRecord>
{
    public void Configure(EntityTypeBuilder<StoredFileRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StoredFiles", AppDbContext.FilesSchema);

        builder.HasKey(file => file.Id);

        builder.Property(file => file.Id).ValueGeneratedNever();

        builder.Property(file => file.OwnerId).IsRequired();

        builder.Property(file => file.Category)
            .HasMaxLength(FileCategory.MaxLength)
            .IsRequired();

        builder.Property(file => file.ObjectKey)
            .HasMaxLength(ObjectKey.MaxLength)
            .IsRequired();

        builder.Property(file => file.Name)
            .HasMaxLength(StoredFileName.MaxLength)
            .IsRequired();

        builder.Property(file => file.DeclaredMediaType)
            .HasMaxLength(DeclaredMediaType.MaxLength)
            .IsRequired();

        builder.Property(file => file.SizeInBytes).IsRequired();

        builder.Property(file => file.Checksum)
            .HasMaxLength(Sha256Checksum.Length)
            .IsRequired();

        builder.Property(file => file.State).IsRequired();
        builder.Property(file => file.RegisteredAt).IsRequired();
        builder.Property(file => file.AvailableAt);
        builder.Property(file => file.InspectionAttempts).IsRequired();
        builder.Property(file => file.NextInspectionAt);

        builder.HasIndex(file => file.ObjectKey)
            .IsUnique()
            .HasDatabaseName("IX_StoredFiles_ObjectKey");

        builder.HasIndex(file => new { file.OwnerId, file.Name, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_Name_Id");

        builder.HasIndex(file => new { file.OwnerId, file.RegisteredAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_RegisteredAt_Id");

        builder.HasIndex(file => new { file.OwnerId, file.AvailableAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_AvailableAt_Id");

        builder.HasIndex(file => new { file.State, file.RegisteredAt })
            .HasDatabaseName("IX_StoredFiles_State_RegisteredAt");

        builder.HasIndex(file => new { file.NextInspectionAt, file.RegisteredAt, file.Id })
            .HasFilter($"\"State\" = {(int)StoredFileState.Deposited}")
            .HasDatabaseName("IX_StoredFiles_Deposited_NextInspectionAt_RegisteredAt_Id");

        builder.HasIndex(file => new { file.OwnerId, file.Category, file.State })
            .IncludeProperties(file => file.SizeInBytes)
            .HasDatabaseName("IX_StoredFiles_OwnerId_Category_State");

        builder.Property(file => file.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(file => file.CreatedAt).IsRequired();
        builder.Property(file => file.CreatedBy);
        builder.Property(file => file.LastModifiedAt);
        builder.Property(file => file.LastModifiedBy);

        builder.HasMany(file => file.Tags)
            .WithOne()
            .HasForeignKey(tag => tag.StoredFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
