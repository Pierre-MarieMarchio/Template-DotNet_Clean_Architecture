using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Configurations;

internal sealed class SpentPasskeyCeremonyConfiguration : IEntityTypeConfiguration<SpentPasskeyCeremonyRecord>
{
    internal const string TableName = "SpentPasskeyCeremonies";

    /// <summary>Base64url SHA-256 is always 43 characters, so the column is fixed width.</summary>
    internal const int StateHashLength = 43;

    public void Configure(EntityTypeBuilder<SpentPasskeyCeremonyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, AuthDbContext.IdentitySchema);

        // The hash is the key, and the key is the single-use guarantee: of two inserts of the same
        // state exactly one lands, whichever instance makes it.
        builder.HasKey(ceremony => ceremony.StateHash);

        builder.Property(ceremony => ceremony.StateHash)
            .HasMaxLength(StateHashLength)
            .IsFixedLength();

        // The purge sweep is a range scan over this column; without the index it is a full table scan.
        builder.HasIndex(ceremony => ceremony.ExpiresAt);
    }
}
