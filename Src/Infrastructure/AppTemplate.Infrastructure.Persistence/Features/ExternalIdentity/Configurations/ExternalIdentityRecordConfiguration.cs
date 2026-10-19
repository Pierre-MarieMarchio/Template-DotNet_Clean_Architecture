using AppTemplate.Application.Features.ExternalIdentity.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Configurations;

/// <summary>
/// Mapping for the subject-to-owner row. The unique index on (issuer, subject) is the whole
/// guarantee that a caller gets one owner: two concurrent first requests both try to insert, and the
/// index refuses the second.
/// </summary>
internal sealed class ExternalIdentityRecordConfiguration : IEntityTypeConfiguration<ExternalIdentityRecord>
{
    public void Configure(EntityTypeBuilder<ExternalIdentityRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ExternalIdentities", AppDbContext.ExternalIdentitySchema);

        builder.HasKey(identity => identity.Id);
        builder.Property(identity => identity.Id).ValueGeneratedNever();

        builder.Property(identity => identity.Issuer)
            .HasMaxLength(ExternalIdentityPolicy.IssuerMaxLength)
            .IsRequired();

        builder.Property(identity => identity.Subject)
            .HasMaxLength(ExternalIdentityPolicy.SubjectMaxLength)
            .IsRequired();

        builder.HasIndex(identity => new { identity.Issuer, identity.Subject })
            .IsUnique()
            .HasDatabaseName("IX_ExternalIdentities_Issuer_Subject");

        builder.Property(identity => identity.UserId).IsRequired();

        // Serves the reads by owner: the reminder recipient lookup, newest first.
        builder.HasIndex(identity => new { identity.UserId, identity.LastSeenAt })
            .HasDatabaseName("IX_ExternalIdentities_UserId_LastSeenAt");

        builder.Property(identity => identity.Email).HasMaxLength(ExternalIdentityPolicy.EmailMaxLength);
        builder.Property(identity => identity.DisplayName).HasMaxLength(ExternalIdentityPolicy.DisplayNameMaxLength);
        builder.Property(identity => identity.CreatedAt).IsRequired();
        builder.Property(identity => identity.LastSeenAt).IsRequired();
    }
}
