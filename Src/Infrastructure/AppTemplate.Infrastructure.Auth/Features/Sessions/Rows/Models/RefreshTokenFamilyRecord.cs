using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;

/// <summary>
/// A session: the family of refresh tokens one sign-in starts, each rotated from the one before.
/// Its id is the <c>sid</c> claim of the access tokens issued in it.
/// <para>
/// <see cref="RevokedAt"/> here is what ends the session, and what every rotation tests inside the
/// same conditional UPDATE that consumes a grant. A successor a rotation stages after the family was
/// revoked is therefore born dead, which is how a revocation wins its race with a rotation.
/// </para>
/// <para>
/// What is kept about the client is deliberately little: its <c>User-Agent</c>, shortened, as of
/// the last use, so the owner can tell one session from another. No address is stored. SECURITY.md
/// says why.
/// </para>
/// </summary>
internal sealed class RefreshTokenFamilyRecord
{
    /// <summary>UUIDv7, like every other key in this model.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required Guid UserId { get; set; }

    /// <summary>When the sign-in that started the session happened.</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a grant of the family was last rotated; <see cref="CreatedAt"/> until then.</summary>
    public required DateTimeOffset LastUsedAt { get; set; }

    /// <summary>The client's <c>User-Agent</c> at the last use, shortened to the column, or <c>null</c>.</summary>
    public string? UserAgent { get; set; }

    /// <summary>When the session was ended: signed out, revoked by its owner, or killed with the rest.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Nullable so a family can be created without materialising the whole user.</summary>
    public AppUser? User { get; set; }

    /// <summary>Every grant of the session, the rotated ones included, until the purge takes them.</summary>
    public ICollection<RefreshToken> Tokens { get; } = [];
}
