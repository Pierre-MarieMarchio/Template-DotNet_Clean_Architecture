namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;

internal interface ISpentPasskeyCeremonyTable
{
    Task<bool> TrySpendAsync(string stateHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
