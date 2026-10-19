namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Models;

/// <summary>
/// One provider subject mapped to one internal owner. Keyed for lookups by the unique pair
/// (<see cref="Issuer"/>, <see cref="Subject"/>); <see cref="UserId"/> is what the business rows
/// carry, and is deliberately not unique: an owner kept across a change of provider has a row per
/// provider, all naming the same id.
/// <para>
/// Not <c>IAuditable</c> and without a concurrency token: its writes are an insert decided by the
/// unique index and a last-writer-wins refresh of values the provider sent, neither of which a
/// version check would make safer.
/// </para>
/// </summary>
internal sealed class ExternalIdentityRecord
{
    public Guid Id { get; set; }

    public string Issuer { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
