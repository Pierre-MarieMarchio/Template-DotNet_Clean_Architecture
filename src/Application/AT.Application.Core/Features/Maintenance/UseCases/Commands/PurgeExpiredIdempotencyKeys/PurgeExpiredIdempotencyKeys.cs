using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public sealed record PurgeExpiredIdempotencyKeysCommand : ICommand<Result<int>>;

public sealed class PurgeExpiredIdempotencyKeysCommandHandler(
    IIdempotencyStore store,
    TimeProvider timeProvider) : ICommandHandler<PurgeExpiredIdempotencyKeysCommand, Result<int>>
{
    public async ValueTask<Result<int>> Handle(PurgeExpiredIdempotencyKeysCommand command, CancellationToken cancellationToken) =>
        await store.PurgeExpiredAsync(timeProvider.GetUtcNow(), cancellationToken);
}
