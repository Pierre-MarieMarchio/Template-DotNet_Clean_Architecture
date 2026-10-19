using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Configurations;

internal sealed class RefreshTokenFamilyConfiguration : IEntityTypeConfiguration<RefreshTokenFamilyRecord>
{
    /// <summary>
    /// A <c>User-Agent</c> is a label an owner reads to tell one session from another, so a full
    /// client string buys nothing over a truncated one and the column stays narrow.
    /// </summary>
    internal const int UserAgentLength = 256;

    public void Configure(EntityTypeBuilder<RefreshTokenFamilyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokenFamilies", AuthDbContext.IdentitySchema);

        builder.HasKey(family => family.Id);

        // The id is a UUIDv7 the model creates, so the database must not try to generate one.
        builder.Property(family => family.Id).ValueGeneratedNever();

        builder.Property(family => family.UserAgent).HasMaxLength(UserAgentLength);

        // The owner's session list scans by user, live sessions only; give it an index.
        builder.HasIndex(family => new { family.UserId, family.RevokedAt });

        // Ending a session is a per-family scan, and so is the revocation test a rotation joins on.
        builder.HasIndex(family => new { family.Id, family.RevokedAt });

        builder.HasOne(family => family.User)
            .WithMany(user => user.RefreshTokenFamilies)
            .HasForeignKey(family => family.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
