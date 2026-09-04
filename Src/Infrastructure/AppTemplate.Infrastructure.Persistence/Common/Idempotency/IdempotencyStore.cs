using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

/// <summary>
/// How many expired rows <see cref="IdempotencyStore.PurgeExpiredAsync"/> deletes per round trip.
/// Under sustained ingestion the expired range can be hundreds of thousands of rows; one
/// unbounded <c>DELETE</c> over all of them holds its lock for the whole scan and leaves that
/// much dead-tuple bloat in a single vacuum-eligible burst. A bounded batch, repeated, keeps each
/// individual lock short and lets autovacuum keep pace.
/// </summary>
public sealed class IdempotencyPurgeOptions
{
    public const string SectionName = "IdempotencyPurge";

    public int BatchSize { get; set; } = 1000;
}

internal sealed class IdempotencyPurgeOptionsValidator : IValidateOptions<IdempotencyPurgeOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyPurgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.BatchSize is < 1 or > 100_000
            ? ValidateOptionsResult.Fail($"'{IdempotencyPurgeOptions.SectionName}:BatchSize' must be between 1 and 100000.")
            : ValidateOptionsResult.Success;
    }
}

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

            var existing = await context.IdempotencyKeys
                .AsNoTracking()
                .SingleAsync(record => record.UserId == key.UserId && record.Key == key.Key, ct);

            return Decide(key, existing);
        }
    }

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
    /// What a lost race means for the loser, read off the row the winner (or an earlier attempt by
    /// the same caller) wrote. Isolated from EF and Npgsql, like
    /// <see cref="RunBatchedDeleteAsync"/>, so the rules it encodes — and the replay it rebuilds from
    /// stored columns — can be exercised without a database.
    /// </summary>
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

    private static bool IsPrimaryKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
