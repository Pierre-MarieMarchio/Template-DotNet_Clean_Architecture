using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Configurations;

internal sealed class SpentPasskeyCeremonyConfiguration : IEntityTypeConfiguration<SpentPasskeyCeremonyRecord>
{
    internal const string TableName = "SpentPasskeyCeremonies";

    internal const int StateHashLength = 43;

    public void Configure(EntityTypeBuilder<SpentPasskeyCeremonyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, AuthDbContext.IdentitySchema);

        builder.HasKey(ceremony => ceremony.StateHash);

        builder.Property(ceremony => ceremony.StateHash)
            .HasMaxLength(StateHashLength)
            .IsFixedLength();

        builder.HasIndex(ceremony => ceremony.ExpiresAt);
    }
}
