using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    internal const int MaxKeyLength = 512;

    internal const int MaxEndpointLength = 256;

    internal const int FingerprintLength = 64;

    internal const int MaxResponseBodyLength = 65536;

    internal const int MaxLocationLength = 2048;

    internal const int MaxETagLength = 128;

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("IdempotencyKeys", AppDbContext.PlatformSchema);

        builder.HasKey(record => new { record.UserId, record.Key });

        builder.Property(record => record.Key).IsRequired().HasMaxLength(MaxKeyLength);
        builder.Property(record => record.Endpoint).IsRequired().HasMaxLength(MaxEndpointLength);

        builder.Property(record => record.Fingerprint)
            .IsRequired()
            .HasMaxLength(FingerprintLength)
            .IsFixedLength();

        builder.Property(record => record.ResponseBody).HasMaxLength(MaxResponseBodyLength);
        builder.Property(record => record.Location).HasMaxLength(MaxLocationLength);
        builder.Property(record => record.ETag).HasMaxLength(MaxETagLength);

        builder.HasIndex(record => record.ExpiresAt);
    }
}
