namespace AppTemplate.Application.Common.Idempotency;

/// <summary>
/// Where a claimed <see cref="IdempotencyKey"/> and its eventual response are kept.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to claim <paramref name="key"/>. Two concurrent claims of the same key can only ever
    /// produce one <see cref="IdempotencyOutcome.Claimed"/>; the loser sees whatever the winner's
    /// row currently says.
    /// </summary>
    Task<IdempotencyClaim> ClaimAsync(IdempotencyKey key, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Records the response of a claimed key, so a later replay can answer with it.</summary>
    Task CompleteAsync(IdempotencyKey key, IdempotentResponse response, CancellationToken ct = default);

    /// <summary>
    /// Gives up a claimed key without recording a response, so a corrected retry under the same key
    /// is not blocked by a failed attempt.
    /// </summary>
    Task ReleaseAsync(IdempotencyKey key, CancellationToken ct = default);

    /// <summary>Deletes every key whose retention window has passed as of <paramref name="asOf"/>.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default);
}
