using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;

internal sealed class SpentPasskeyCeremonyTable(AuthDbContext context) : ISpentPasskeyCeremonyTable
{
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
