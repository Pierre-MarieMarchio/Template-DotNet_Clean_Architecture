namespace AppTemplate.Infrastructure.Auth.Features.Auth.Tables;

/// <summary>
/// The storage half of refresh-token handling: grant rows in, grant rows out, and nothing about
/// policy.
/// <para>
/// This is the seam that keeps every decision in the identity module — a token is 32 CSPRNG bytes,
/// only its SHA-256 hash is persisted, every presentation rotates the grant, and a replayed token
/// kills the whole family — while the reading and writing of rows happens here. The interface is
/// deliberately narrow and speaks only in hashes and instants: the row type never leaves this
/// assembly, so nothing outside it can write a grant that skipped those decisions.
/// </para>
/// <para>
/// A <em>table</em> rather than a repository or a store. <c>docs/DECISIONS.md</c> sets out what each
/// of those four storage words names and why this contract is a table;
/// <c>StorageVocabularyTests</c> holds the distinction.
/// </para>
/// <para>
/// The lifecycle of a session — the family these grants belong to — is
/// <see cref="IRefreshTokenFamilyTable"/>'s, not this one's: a grant is created into a family this
/// table does not start and cannot end.
/// </para>
/// <para>
/// Public because it crosses an assembly boundary. Every method stages and <c>IUnitOfWork</c>
/// commits, with the one exception stated on <see cref="TryRotateAsync"/>: one context serves the
/// request, so a rotation and whatever else it staged commit together.
/// </para>
/// </summary>
public interface IRefreshTokenTable
{
    /// <summary>Stages a grant as the newest one of its session.</summary>
    void Add(Guid userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt);

    /// <summary>
    /// The grant with this hash, or <c>null</c> when there is none. Returns revoked and expired grants
    /// too, and the state of the grant's session: the caller has to tell "unknown" from "already
    /// used" from "session ended" in order to detect a replay without alarming a client that merely
    /// holds a token of an ended session, even though it must answer the client identically for
    /// each of the first two.
    /// </summary>
    Task<RefreshTokenGrant?> FindAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a live grant and stages its successor in the same session: the presented grant is
    /// marked revoked and its successor's hash recorded so the chain stays auditable, then the
    /// successor row is staged.
    /// <para>
    /// The consumption is one conditional UPDATE that commits on its own, and that is the exception to
    /// "every method stages". It is what makes rotation single-use: the liveness test travels in the
    /// UPDATE's own WHERE clause, so of two simultaneous presentations exactly one affects a row.
    /// Testing liveness on a preceding read cannot achieve that, because the UPDATE that EF derives
    /// from a tracked row is keyed on the primary key alone and both presentations would pass. The
    /// successor is only staged, so a commit that never happens leaves the grant consumed with no
    /// replacement issued — the client has to authenticate again, which is the safe direction.
    /// </para>
    /// <para>
    /// The liveness test is the grant's alone, never the session's: whether the session may still
    /// rotate was answered when the grant was read, and a session revoked while this UPDATE runs
    /// still gets its successor — one that is dead on arrival, because the next presentation finds
    /// the family revoked. This is how a revocation wins its race with a rotation without holding a
    /// lock across the two.
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
    /// Deletes every grant that expired at or before <paramref name="cutoff"/>, committing on its
    /// own rather than through <c>IUnitOfWork</c> — like <see cref="TryRotateAsync"/>, a housekeeping
    /// sweep has no request to share a transaction with. Sessions whose every grant went this way
    /// are <see cref="IRefreshTokenFamilyTable.PurgeChildlessAsync"/>'s to delete.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
