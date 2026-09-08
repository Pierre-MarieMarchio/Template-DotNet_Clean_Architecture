using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

/// <summary>
/// Deletes every idempotency key whose retention window has passed. Administrative rather than
/// user-facing: nothing about a caller's own request needs this, only the operator running the
/// store's housekeeping.
/// </summary>
public sealed class PurgeExpiredIdempotencyKeysUseCase(
    IIdempotencyStore store,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredIdempotencyKeysUseCase
{
    /// <inheritdoc />
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await store.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
