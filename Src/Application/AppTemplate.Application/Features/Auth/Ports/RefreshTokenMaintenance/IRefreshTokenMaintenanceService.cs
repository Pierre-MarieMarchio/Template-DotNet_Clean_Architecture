using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;

namespace AppTemplate.Application.Features.Auth.Ports.RefreshTokenMaintenance;

/// <summary>
/// Housekeeping for the refresh-token grant table, kept apart from <see cref="IRefreshTokenGrantsService"/>
/// because purging old rows is not an authentication capability — nothing about a caller's own
/// request needs it, only the operator running the store's maintenance.
/// </summary>
public interface IRefreshTokenMaintenanceService
{
    /// <summary>
    /// Deletes every grant whose retention window has passed as of <paramref name="asOf"/>. What
    /// counts as "passed" — how long an expired grant is kept before it is actually deleted — is a
    /// configuration decision the adapter behind this port makes, not this caller: keeping expired
    /// grants around for a while is what makes a replayed, already-rotated token detectable instead
    /// of merely unknown.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
