using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenMaintenance;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;

/// <summary>
/// Deletes every refresh-token grant whose retention window has passed. Administrative rather than
/// user-facing, like
/// <see cref="PurgeExpiredIdempotencyKeys.PurgeExpiredIdempotencyKeysUseCase"/>: nothing about a
/// caller's own request needs this, only the operator running the store's housekeeping. Left
/// unpurged, this table grows by one row per rotation forever — see
/// <see cref="IRefreshTokenMaintenanceService"/> for why a grant still lingers a while after it expires
/// rather than being deleted immediately.
/// </summary>
public sealed class PurgeExpiredRefreshTokensUseCase(
    IRefreshTokenMaintenanceService maintenance,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredRefreshTokensUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await maintenance.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
