using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Configurations;

internal sealed class AccessKeyConfiguration : IEntityTypeConfiguration<AccessKeyRecord>
{
    internal const string TableName = "AccessKeys";

    internal const int KeyHashLength = 43;

    internal const int ScopesLength = 64;

    internal const char ScopeSeparator = ' ';

    public void Configure(EntityTypeBuilder<AccessKeyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, AuthDbContext.IdentitySchema);

        builder.HasKey(accessKey => accessKey.Id);

        builder.Property(accessKey => accessKey.Id).ValueGeneratedNever();

        builder.Property(accessKey => accessKey.Name)
            .IsRequired()
            .HasMaxLength(AccessKeyPolicy.NameMaxLength);

        builder.Property(accessKey => accessKey.KeyHash)
            .IsRequired()
            .HasMaxLength(KeyHashLength)
            .IsFixedLength();

        builder.HasIndex(accessKey => accessKey.KeyHash).IsUnique();

        builder.Property(accessKey => accessKey.Prefix)
            .IsRequired()
            .HasMaxLength(AccessKeyPolicy.DisplayPrefixLength)
            .IsFixedLength();

        builder.Property(accessKey => accessKey.Scopes)
            .IsRequired()
            .HasMaxLength(ScopesLength);

        builder.HasIndex(accessKey => new { accessKey.UserId, accessKey.RevokedAt });

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(accessKey => accessKey.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
