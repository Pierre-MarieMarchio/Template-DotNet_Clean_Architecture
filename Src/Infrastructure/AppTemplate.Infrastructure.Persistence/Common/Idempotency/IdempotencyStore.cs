using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

internal sealed class IdempotencyStore(
    IDbContextFactory<AppDbContext> contextFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<IdempotencyPurgeOptions> purgeOptions,
    ILogger<IdempotencyStore> logger) : IIdempotencyStore
{
    private const int _noContentStatusCode = 204;

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
            entry.State = EntityState.Detached;

            return await ClaimExistingAsync(context, key, expiresAt, claimedUntil, ct);
        }
    }

    private async Task<IdempotencyClaim> ClaimExistingAsync(
        AppDbContext context,
        IdempotencyKey key,
        DateTimeOffset expiresAt,
        DateTimeOffset claimedUntil,
        CancellationToken ct)
    {
        var existing = await ReadAsync(context, key, ct);

        if (!string.Equals(existing.Fingerprint, key.Fingerprint, StringComparison.Ordinal))
        {
            return IdempotencyClaim.KeyReused();
        }

        if (!HasExpiredLease(existing, dateTimeProvider.UtcNow))
        {
            return Decide(key, existing);
        }

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

    public async Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        int batchSize = purgeOptions.Value.BatchSize;

        int total = await RunBatchedDeleteAsync(
            batchSize,
            batchCt => DeleteBatchAsync(asOf, batchSize, batchCt),
            ct);

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

        return await context.IdempotencyKeys
            .Where(record => record.ExpiresAt <= asOf)
            .OrderBy(record => record.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
    }

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

        if (existing.ResponseBody is null && existing.StatusCode != _noContentStatusCode)
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

    internal static bool HasExpiredLease(IdempotencyRecord existing, DateTimeOffset now) =>
        !existing.IsCompleted && existing.ClaimedUntil <= now;

    private static bool IsPrimaryKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
