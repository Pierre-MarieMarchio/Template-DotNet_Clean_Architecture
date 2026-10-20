namespace AppTemplate.Application.Core.Common.Idempotency;

public interface IIdempotencyStore
{
    Task<IdempotencyClaim> ClaimAsync(
        IdempotencyKey key,
        DateTimeOffset expiresAt,
        DateTimeOffset claimedUntil,
        CancellationToken ct = default);

    Task CompleteAsync(IdempotencyKey key, IdempotentResponse response, CancellationToken ct = default);

    Task ReleaseAsync(IdempotencyKey key, CancellationToken ct = default);

    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default);
}
