using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands;

public interface IPurgeExpiredRefreshTokensUseCase : IUseCase<Result<int>>;

/// <summary>
/// Deletes every refresh-token grant whose retention window has passed. Administrative rather than
/// user-facing, like <see cref="PurgeExpiredIdempotencyKeysUseCase"/>: nothing about a caller's own
/// request needs this, only the operator running the store's housekeeping. Left unpurged, this table
/// grows by one row per rotation forever — see <see cref="IRefreshTokenMaintenance"/> for why a
/// grant still lingers a while after it expires rather than being deleted immediately.
/// </summary>
public sealed class PurgeExpiredRefreshTokensUseCase(
    IRefreshTokenMaintenance maintenance,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredRefreshTokensUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await maintenance.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
