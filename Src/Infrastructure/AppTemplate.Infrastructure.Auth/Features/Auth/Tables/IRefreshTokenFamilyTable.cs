using AppTemplate.Application.Auth.Features.Auth.Ports.Sessions;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Tables;

/// <summary>The storage half of session handling: one row per sign-in, holding what its owner lists
/// it by. A session is a refresh-token family — the chain of grants one sign-in starts — so what ends
/// a session is the family row's revocation, and every rotation tests it; the grant table
/// (<see cref="IRefreshTokenTable"/>) creates its rows into a family this one started and cannot end.
/// Ending a session is one conditional UPDATE on the family row, then one on its live grants —
/// separate statements, not atomic, and deliberately in that order: a process dying between them
/// leaves grants that are dead on arrival, because every rotation tests the family first. Public for
/// the same reason <see cref="IRefreshTokenTable"/> is, and a table for the same one.</summary>
public interface IRefreshTokenFamilyTable
{
    /// <summary>
    /// Stages the session a new sign-in starts and returns its id, which the caller puts in the
    /// first grant and in the access token issued beside it. The id exists before any commit: it is
    /// a client-side UUIDv7, not a database sequence value.
    /// </summary>
    Guid Start(Guid userId, DateTimeOffset createdAt, string? userAgent);

    /// <summary>Records a rotation as the session's latest use. Commits on its own and silently
    /// does nothing to a session ended meanwhile: the family's <c>RevokedAt</c> travels in the
    /// UPDATE's WHERE clause, so a revocation that commits first wins.</summary>
    Task MarkUsedAsync(Guid familyId, string? userAgent, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Ends one of an account's sessions and revokes its live grants, committing on its
    /// own. Idempotent: ending a session already ended succeeds and changes nothing.</summary>
    /// <returns><c>false</c> when the account has no session with that id — which is also the answer
    /// for a session that belongs to somebody else, so the caller is not told which of the two it
    /// was.</returns>
    Task<bool> EndAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every live session of an account but <paramref name="exceptFamilyId"/>, revoking their
    /// live grants too, and commits on its own. An id naming no live session of the account spares
    /// nothing — the exception travels in each statement's WHERE clause, so it matches no row and
    /// every session ends.
    /// </summary>
    Task RevokeAllForUserAsync(
        Guid userId,
        Guid? exceptFamilyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's live sessions — neither revoked nor past the expiry of their newest grant —
    /// most recently used first.
    /// </summary>
    Task<IReadOnlyList<ActiveSession>> ListLiveAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every session whose grants the purge has already taken, committing on its own for
    /// the reason <see cref="IRefreshTokenTable.PurgeExpiredAsync"/> gives. A session keeps no
    /// history once nothing can be replayed against it.
    /// </summary>
    Task<int> PurgeChildlessAsync(CancellationToken cancellationToken = default);
}
