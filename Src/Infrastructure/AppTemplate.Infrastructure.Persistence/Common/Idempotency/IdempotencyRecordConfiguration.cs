using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

/// <summary>Mapping for the idempotency-key claim table.</summary>
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <summary>The largest header value <c>IdempotencyOptions.MaxKeyLength</c> may ever be configured to.</summary>
    internal const int MaxKeyLength = 512;

    /// <summary>Method and path, e.g. <c>POST /api/v1/todo-lists/{id}/items</c>.</summary>
    internal const int MaxEndpointLength = 256;

    /// <summary>Hex SHA-256 is always 64 characters.</summary>
    internal const int FingerprintLength = 64;

    /// <summary>
    /// A physical ceiling on the stored response body, independent of the runtime-configurable
    /// <c>IdempotencyOptions.MaxStoredResponseBytes</c>. The filter enforces the configured limit
    /// before ever writing a row; this is the column's own bound, generous enough for the shipped
    /// default (8 KB) with headroom, and would need a migration to raise further.
    /// </summary>
    internal const int MaxResponseBodyLength = 65536;

    internal const int MaxLocationLength = 2048;

    /// <summary>
    /// A strong validator issued here is a quoted Base64Url of a 32-bit version — eight characters.
    /// The bound is far above that so a future encoding, or a longer tag minted elsewhere, fits
    /// without a migration, while staying small enough that the column can hold nothing but a
    /// validator.
    /// </summary>
    internal const int MaxETagLength = 128;

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("IdempotencyKeys", AppDbContext.PlatformSchema);

        // Composite, not a surrogate id: this is what makes ClaimAsync's insert race-safe. Two
        // concurrent claims of the same (UserId, Key) collide on the primary key itself rather than
        // one silently overwriting the other, so the loser can be told what the winner decided
        // instead of both believing they own the claim.
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

        // The purge is a range scan over this column; without the index it would be a full table scan.
        builder.HasIndex(record => record.ExpiresAt);
    }
}
