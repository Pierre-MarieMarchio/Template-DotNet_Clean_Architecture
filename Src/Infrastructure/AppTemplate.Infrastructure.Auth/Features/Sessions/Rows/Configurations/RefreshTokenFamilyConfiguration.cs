using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Configurations;

internal sealed class RefreshTokenFamilyConfiguration : IEntityTypeConfiguration<RefreshTokenFamilyRecord>
{
    internal const int UserAgentLength = 256;

    public void Configure(EntityTypeBuilder<RefreshTokenFamilyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokenFamilies", AuthDbContext.IdentitySchema);

        builder.HasKey(family => family.Id);

        builder.Property(family => family.Id).ValueGeneratedNever();

        builder.Property(family => family.UserAgent).HasMaxLength(UserAgentLength);

        builder.HasIndex(family => new { family.UserId, family.RevokedAt });

        builder.HasIndex(family => new { family.Id, family.RevokedAt });

        builder.HasOne(family => family.User)
            .WithMany(user => user.RefreshTokenFamilies)
            .HasForeignKey(family => family.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
