namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Models;

/// <summary>
/// A single refresh-token grant. Only the SHA-256 hash of the secret is stored, so a database
/// disclosure yields no usable tokens, and lookup goes through a unique index on a fixed-width
/// column instead of the former unindexed <c>nvarchar(max)</c> table scan joined to the user.
/// <para>
/// Internal, unlike <see cref="AppUser"/>: nothing forces it to be visible. The identity module reaches
/// grants through <see cref="Stores.IRefreshTokenStore"/>, which speaks in hashes and instants and never
/// hands this type out. A row type that leaves this assembly is a row somebody can write without going
/// through the rules that govern it.
/// </para>
/// </summary>
internal sealed class RefreshToken
{
    /// <summary>UUIDv7, like every other key in this model: sequential, so index inserts stay local.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Base64url SHA-256 of the raw token. The raw value only ever exists in a response.</summary>
    public required string TokenHash { get; set; }

    public required Guid UserId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token is rotated, revoked on logout, or killed as part of a family.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The successor issued when this token was rotated, which makes the chain auditable.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Nullable so a grant can be created without materialising the whole user.</summary>
    public AppUser? User { get; set; }

    /// <summary>Expiry is compared explicitly on every presentation; it used to be stored and ignored.</summary>
    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}
