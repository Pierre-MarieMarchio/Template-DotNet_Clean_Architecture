namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Stores;

/// <summary>
/// The storage half of refresh-token handling: rows in, rows out, and nothing about policy.
/// <para>
/// This is the seam that let the identity module keep its authentication concerns while its store moved
/// here. What stayed there is every decision — a token is 32 CSPRNG bytes, only its SHA-256 hash is
/// persisted, every presentation rotates the grant, and a replayed token kills the whole family. What
/// moved here is the reading and writing of rows. The interface is deliberately narrow and speaks only
/// in hashes and instants: the row type never leaves this assembly, so nothing outside it can write a
/// grant that skipped those decisions.
/// </para>
/// <para>
/// A store contract normally belongs in <c>AppTemplate.Domain</c> under <c>Features/&lt;Feature&gt;/Stores</c>,
/// beside the aggregate it loads. This one is the deliberate exception: a refresh token has no domain
/// presence at all — it is a credential the authentication adapter mints and consumes — so there is no
/// aggregate for it to sit beside, and it is declared here instead.
/// </para>
/// <para>
/// Public because it crosses an assembly boundary. Every method stages and <c>IUnitOfWork</c> commits,
/// with the one exception stated on <see cref="TryRotateAsync"/>. That is a change from the previous
/// design — the identity store used to call <c>SaveChangesAsync</c> itself, because it had a context of
/// its own and no shared transaction to join. There is one context now, so a rotation and whatever else
/// the request staged genuinely commit together.
/// </para>
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Stages a new grant for a user.</summary>
    void Add(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt);

    /// <summary>
    /// The grant with this hash, or <c>null</c> when there is none. Returns revoked and expired grants
    /// too: the caller has to tell "unknown" from "already used" in order to detect a replay, even though
    /// it must answer the client identically either way.
    /// </summary>
    Task<RefreshTokenGrant?> FindAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a live grant and stages its successor: the presented grant is marked revoked and its
    /// successor's hash recorded so the chain stays auditable, then the successor row is staged.
    /// <para>
    /// The consumption is one conditional UPDATE that commits on its own, and that is the exception to
    /// "every method stages". It is what makes rotation single-use: the liveness test travels in the
    /// UPDATE's own WHERE clause, so of two simultaneous presentations exactly one affects a row.
    /// Testing liveness on a preceding read cannot achieve that, because the UPDATE that EF derives
    /// from a tracked row is keyed on the primary key alone and both presentations would pass. The
    /// successor is only staged, so a commit that never happens leaves the grant consumed with no
    /// replacement issued — the client has to authenticate again, which is the safe direction.
    /// </para>
    /// </summary>
    /// <returns><c>false</c> when the presented hash names no live grant: unknown, expired, already
    /// revoked, or consumed a moment ago by a concurrent presentation. Nothing is staged in that case.
    /// </returns>
    Task<bool> TryRotateAsync(
        string presentedTokenHash,
        string replacementTokenHash,
        DateTimeOffset now,
        DateTimeOffset replacementExpiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the revocation of a live grant. Silent when the hash is unknown or already revoked, so
    /// logging out cannot be used to probe for valid tokens.
    /// </summary>
    Task RevokeAsync(string tokenHash, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the revocation of every live grant for a user: theft response, and the hook for
    /// "sign out everywhere".
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every grant that expired at or before <paramref name="cutoff"/>, committing on its
    /// own rather than through <c>IUnitOfWork</c> — like <see cref="TryRotateAsync"/>, a housekeeping
    /// sweep has no request to share a transaction with.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
