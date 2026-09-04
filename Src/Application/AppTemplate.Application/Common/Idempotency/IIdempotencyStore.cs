namespace AppTemplate.Application.Common.Idempotency;

/// <summary>
/// Where a claimed <see cref="IdempotencyKey"/> and its eventual response are kept.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to claim <paramref name="key"/>. Two concurrent claims of the same key can only ever
    /// produce one <see cref="IdempotencyStatus.Claimed"/>; the loser sees whatever the winner's row
    /// currently says — including when the "winner" is a retry reclaiming a lease the original
    /// claimant never gave up.
    /// </summary>
    /// <param name="expiresAt">
    /// How long a <em>completed</em> response stays replayable — the retention window. Unrelated to
    /// <paramref name="claimedUntil"/>: this one only ever matters once the claim is done.
    /// </param>
    /// <param name="claimedUntil">
    /// How long an <em>unfinished</em> claim blocks a retry before it is treated as abandoned — the
    /// claimant's process died before it could call <see cref="CompleteAsync"/> or
    /// <see cref="ReleaseAsync"/> — and made reclaimable by whoever asks next.
    /// </param>
    Task<IdempotencyClaim> ClaimAsync(
        IdempotencyKey key,
        DateTimeOffset expiresAt,
        DateTimeOffset claimedUntil,
        CancellationToken ct = default);

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
