using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public sealed class PurgeExpiredIdempotencyKeysUseCase(
    IIdempotencyStore store,
    IDateTimeProvider dateTimeProvider) : IPurgeExpiredIdempotencyKeysUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        await store.PurgeExpiredAsync(dateTimeProvider.UtcNow, cancellationToken);
}
