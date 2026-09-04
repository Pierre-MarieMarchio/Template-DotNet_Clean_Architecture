using AppTemplate.Application.Common.Idempotency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

/// <summary>
/// Claims, completes, releases and purges idempotency keys. Internal, like every adapter here: the
/// application layer depends only on <see cref="IIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// <b>Why a separate <see cref="IDbContextFactory{TContext}"/> and not the request's shared
/// <see cref="AppDbContext"/>.</b> <c>IUnitOfWork</c> is documented as the only thing allowed to
/// commit the request's context, and every write here would otherwise have to go through it. But a
/// claim is not part of the business transaction: it must survive independently of whether the use
/// case that follows it commits, and releasing a claim after a failed action must not roll back a
/// domain write that already committed on its own. Those are two different transactions by nature,
/// not by convenience, so each method here opens and disposes its own short-lived context built from
/// the same connection options rather than touching the ambient one.
/// </remarks>
internal sealed class IdempotencyStore(
    IDbContextFactory<AppDbContext> contextFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<IdempotencyPurgeOptions> purgeOptions,
    ILogger<IdempotencyStore> logger) : IIdempotencyStore
{
    public async Task<IdempotencyClaim> ClaimAsync(
        IdempotencyKey key,
        DateTimeOffset expiresAt,
        DateTimeOffset claimedUntil,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        EntityEntry<IdempotencyRecord> entry = context.IdempotencyKeys.Add(new IdempotencyRecord
        {
            UserId = key.UserId,
            Key = key.Key,
            Endpoint = key.Endpoint,
            Fingerprint = key.Fingerprint,
            IsCompleted = false,
            ClaimedUntil = claimedUntil,
            CreatedAt = dateTimeProvider.UtcNow,
            ExpiresAt = expiresAt,
        });

        try
        {
            await context.SaveChangesAsync(ct);
            return IdempotencyClaim.Claimed();
        }
        catch (DbUpdateException exception) when (IsPrimaryKeyViolation(exception))
        {
            // The failed insert is still tracked as Added. Left alone, the next SaveChangesAsync on
            // this context — there is none here, but the pattern is copied wherever this file is
            // read from — would retry the exact same doomed insert instead of the read below ever
            // running. Detaching it is what makes the re-read safe.
            entry.State = EntityState.Detached;

            return await ClaimExistingAsync(context, key, expiresAt, claimedUntil, ct);
        }
    }

    /// <summary>
    /// What the loser of the initial insert race does next: read the row it collided with, and either
    /// accept its verdict or — when that row is an unfinished claim whose lease has run out — reclaim
    /// it for this attempt instead of reporting <see cref="IdempotencyStatus.InProgress"/> forever.
    /// </summary>
    private async Task<IdempotencyClaim> ClaimExistingAsync(
        AppDbContext context,
        IdempotencyKey key,
        DateTimeOffset expiresAt,
        DateTimeOffset claimedUntil,
        CancellationToken ct)
    {
        var existing = await ReadAsync(context, key, ct);

        // A fingerprint mismatch means the same key string was reused for a genuinely different
        // request — a client error, not an abandoned claim. Never reclaim on the strength of that,
        // no matter how stale the row looks.
        if (!string.Equals(existing.Fingerprint, key.Fingerprint, StringComparison.Ordinal))
        {
            return IdempotencyClaim.KeyReused();
        }

        if (!HasExpiredLease(existing, dateTimeProvider.UtcNow))
        {
            return Decide(key, existing);
        }

        // Same two-participant rendezvous as RefreshTokenTable.TryRotateAsync: the WHERE clause
        // restates every condition that made the row reclaimable, so the database — not this read —
        // decides which of two simultaneous retries wins. Zero rows affected means we lost.
        int reclaimed = await context.IdempotencyKeys
            .Where(record =>
                record.UserId == key.UserId
                && record.Key == key.Key
                && !record.IsCompleted
                && record.ClaimedUntil <= dateTimeProvider.UtcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.ClaimedUntil, claimedUntil)
                    .SetProperty(record => record.ExpiresAt, expiresAt),
                ct);

        if (reclaimed > 0)
        {
            return IdempotencyClaim.Claimed();
        }

        // Lost the reclaim race: another retry got there first, and may since have completed or
        // renewed the lease again. Whatever its row says now is final enough to answer with — a
        // caller that disagrees will simply retry.
        return Decide(key, await ReadAsync(context, key, ct));
    }

    private static Task<IdempotencyRecord> ReadAsync(AppDbContext context, IdempotencyKey key, CancellationToken ct) =>
        context.IdempotencyKeys
            .AsNoTracking()
            .SingleAsync(record => record.UserId == key.UserId && record.Key == key.Key, ct);

    public async Task CompleteAsync(IdempotencyKey key, IdempotentResponse response, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(response);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        int updated = await context.IdempotencyKeys
            .Where(record => record.UserId == key.UserId && record.Key == key.Key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.IsCompleted, true)
                    .SetProperty(record => record.StatusCode, response.StatusCode)
                    .SetProperty(record => record.ResponseBody, response.Body)
                    .SetProperty(record => record.Location, response.Location)
                    .SetProperty(record => record.ETag, response.ETag),
                ct);

        if (updated == 0)
        {
            throw new InvalidOperationException(
                $"No claimed idempotency key '{key.Key}' for user '{key.UserId}' was found to complete.");
        }
    }

    public async Task ReleaseAsync(IdempotencyKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var context = await contextFactory.CreateDbContextAsync(ct);

        await context.IdempotencyKeys
            .Where(record => record.UserId == key.UserId && record.Key == key.Key)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Deletes every row whose retention window has passed, in bounded batches rather than one
    /// <c>DELETE</c> over the whole expired range — see <see cref="IdempotencyPurgeOptions"/> for
    /// why. Each batch opens its own short-lived context, same as every other method here.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        int batchSize = purgeOptions.Value.BatchSize;

        int total = await RunBatchedDeleteAsync(
            batchSize,
            batchCt => DeleteBatchAsync(asOf, batchSize, batchCt),
            ct);

        // The IsEnabled guard keeps this off the hot path when Information logging is off (CA1873);
        // total > 0 additionally keeps a no-op purge silent instead of logging every empty sweep.
        if (total > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Purged {Total} expired idempotency key(s) in batches of {BatchSize}.",
                total,
                batchSize);
        }

        return total;
    }

    private async Task<int> DeleteBatchAsync(DateTimeOffset asOf, int batchSize, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        // Ordered by ExpiresAt, which is already indexed for this exact scan (see
        // IdempotencyRecordConfiguration), so the oldest-expired rows go first and each batch is a
        // short, index-driven range delete rather than a scan of the whole expired set.
        return await context.IdempotencyKeys
            .Where(record => record.ExpiresAt <= asOf)
            .OrderBy(record => record.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// The looping and summing rule, isolated from EF and Npgsql so it can be exercised without a
    /// database: keep asking for another batch of at most <paramref name="batchSize"/> rows, and
    /// stop the moment a batch comes back smaller than that — which is the only way to tell "the
    /// table still has more" apart from "that was the last one" without a second round trip.
    /// </summary>
    internal static async Task<int> RunBatchedDeleteAsync(
        int batchSize,
        Func<CancellationToken, Task<int>> deleteBatchAsync,
        CancellationToken ct)
    {
        int total = 0;

        while (true)
        {
            int deleted = await deleteBatchAsync(ct);
            total += deleted;

            if (deleted < batchSize)
            {
                return total;
            }
        }
    }

    /// <summary>
    /// What a lost race means for the loser, read off the row the winner (an earlier attempt by the
    /// same caller, or — once <see cref="ClaimExistingAsync"/> has ruled out a reclaim — whoever
    /// currently holds the lease) wrote. Isolated from EF and Npgsql, like
    /// <see cref="RunBatchedDeleteAsync"/>, so the rules it encodes — and the replay it rebuilds from
    /// stored columns — can be exercised without a database.
    /// </summary>
    /// <remarks>
    /// Deliberately does not look at <see cref="IdempotencyRecord.ClaimedUntil"/>: by the time this
    /// runs, <see cref="ClaimExistingAsync"/> has already established that either the lease is still
    /// valid, or a reclaim attempt against it just lost. Either way "still running" is the right
    /// answer here, with no further lease arithmetic to repeat.
    /// </remarks>
    internal static IdempotencyClaim Decide(IdempotencyKey key, IdempotencyRecord existing)
    {
        if (!string.Equals(existing.Fingerprint, key.Fingerprint, StringComparison.Ordinal))
        {
            return IdempotencyClaim.KeyReused();
        }

        if (!existing.IsCompleted)
        {
            return IdempotencyClaim.InProgress();
        }

        if (existing.ResponseBody is null)
        {
            return IdempotencyClaim.NotReplayable();
        }

        return IdempotencyClaim.Replay(
            new IdempotentResponse(
                existing.StatusCode!.Value,
                existing.ResponseBody,
                existing.Location,
                existing.ETag));
    }

    /// <summary>
    /// Whether an unfinished claim's lease has run out, and it is therefore fair game for
    /// <see cref="ClaimExistingAsync"/> to reclaim on behalf of a new retry instead of reporting
    /// <see cref="IdempotencyStatus.InProgress"/> for the rest of the row's retention window.
    /// Isolated from EF, like <see cref="Decide"/>, so it can be exercised without a database.
    /// </summary>
    internal static bool HasExpiredLease(IdempotencyRecord existing, DateTimeOffset now) =>
        !existing.IsCompleted && existing.ClaimedUntil <= now;

    private static bool IsPrimaryKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
