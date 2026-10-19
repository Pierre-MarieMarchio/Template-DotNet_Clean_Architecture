using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;

/// <summary>
/// Reads and writes spent-ceremony rows. Internal, like its contract and the row type it handles.
/// </summary>
internal sealed class SpentPasskeyCeremonyTable(AuthDbContext context) : ISpentPasskeyCeremonyTable
{
    /// <summary>
    /// <c>ON CONFLICT DO NOTHING</c> rather than an added row and a caught unique violation: the
    /// statement bypasses the change tracker, so it neither commits whatever else the request's
    /// context has staged nor leaves a failed insert tracked for the next save to retry, and the
    /// loser of a race is told by the affected-row count instead of by an exception.
    /// </summary>
    private const string _spend =
        $$"""
        INSERT INTO "{{AuthDbContext.IdentitySchema}}"."{{SpentPasskeyCeremonyConfiguration.TableName}}"
            ("{{nameof(SpentPasskeyCeremonyRecord.StateHash)}}", "{{nameof(SpentPasskeyCeremonyRecord.ExpiresAt)}}")
        VALUES ({0}, {1})
        ON CONFLICT ("{{nameof(SpentPasskeyCeremonyRecord.StateHash)}}") DO NOTHING
        """;

    public async Task<bool> TrySpendAsync(
        string stateHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateHash);

        int inserted = await context.Database.ExecuteSqlRawAsync(_spend, [stateHash, expiresAt], cancellationToken);

        return inserted == 1;
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await context.SpentPasskeyCeremonies
            .Where(ceremony => ceremony.ExpiresAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
