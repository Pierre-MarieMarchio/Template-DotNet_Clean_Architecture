using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Idempotency;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands;

public interface IPurgeExpiredIdempotencyKeysUseCase : IUseCase<Result<int>>;

/// <summary>
/// Deletes every idempotency key whose retention window has passed. Administrative rather than
/// user-facing: nothing about a caller's own request needs this, only the operator running the
/// store's housekeeping.
/// </summary>
public sealed class PurgeExpiredIdempotencyKeysUseCase(
    IIdempotencyStore store,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredIdempotencyKeysUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await store.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
