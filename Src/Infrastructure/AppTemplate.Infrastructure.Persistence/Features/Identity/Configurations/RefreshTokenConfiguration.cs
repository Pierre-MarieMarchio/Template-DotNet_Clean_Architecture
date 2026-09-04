using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Configurations;

/// <summary>Mapping for the refresh-token grant table.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>Base64url SHA-256 is always 43 characters, so the column is fixed width.</summary>
    internal const int TokenHashLength = 43;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens", AppDbContext.IdentitySchema);

        builder.HasKey(token => token.Id);

        // The id is a UUIDv7 the model creates, so the database must not try to generate one.
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash)
            .IsRequired()
            .HasMaxLength(TokenHashLength)
            .IsFixedLength();

        builder.HasIndex(token => token.TokenHash).IsUnique();

        builder.Property(token => token.ReplacedByTokenHash)
            .HasMaxLength(TokenHashLength)
            .IsFixedLength();

        // Revoking a whole token family is a per-user scan; give it an index.
        builder.HasIndex(token => new { token.UserId, token.RevokedAt });

        // The purge sweep is a range scan over this column; without the index it is a full table scan.
        builder.HasIndex(token => token.ExpiresAt);

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
