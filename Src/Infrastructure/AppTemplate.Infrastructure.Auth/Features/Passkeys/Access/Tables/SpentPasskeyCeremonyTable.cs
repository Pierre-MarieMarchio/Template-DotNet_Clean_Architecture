using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;

internal sealed class SpentPasskeyCeremonyTable(AuthDbContext context, DbContextOptions<AuthDbContext> options)
    : ISpentPasskeyCeremonyTable
{
    public async Task<bool> TrySpendAsync(
        string stateHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateHash);

        await using var ledger = new AuthDbContext(options);
        ledger.SpentPasskeyCeremonies.Add(new SpentPasskeyCeremonyRecord { StateHash = stateHash, ExpiresAt = expiresAt });

        try
        {
            await ledger.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseProvider.IsUniqueViolation(exception))
        {
            return false;
        }

        return true;
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await context.SpentPasskeyCeremonies
            .Where(ceremony => ceremony.ExpiresAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
