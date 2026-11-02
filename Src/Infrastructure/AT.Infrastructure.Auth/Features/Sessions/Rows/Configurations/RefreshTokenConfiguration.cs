using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    internal const int TokenHashLength = 43;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens", AuthDbContext.IdentitySchema);

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(TokenHashLength)
            .IsFixedLength();

        builder.HasIndex(token => token.TokenHash).IsUnique();

        builder.Property(token => token.ReplacedByTokenHash)
            .HasMaxLength(TokenHashLength)
            .IsFixedLength();

        builder.HasIndex(token => new { token.UserId, token.RevokedAt });

        builder.HasIndex(token => new { token.FamilyId, token.RevokedAt });

        builder.HasIndex(token => token.ExpiresAt);

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(token => token.Family)
            .WithMany(family => family.Tokens)
            .HasForeignKey(token => token.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
