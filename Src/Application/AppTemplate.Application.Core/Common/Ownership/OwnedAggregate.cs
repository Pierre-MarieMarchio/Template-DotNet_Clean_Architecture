using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ownership;

public static class OwnedAggregate
{
    public static Result<TAggregate> Require<TAggregate>(
        TAggregate? aggregate,
        UserId ownerId,
        Error notFound,
        VersionPrecondition? precondition)
        where TAggregate : class, IOwnedAggregate, IVersioned
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(notFound);

        if (aggregate is null || aggregate.OwnerId != ownerId)
        {
            return Result.Failure<TAggregate>(notFound);
        }

        if (precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version))
        {
            return Result.Failure<TAggregate>(ConcurrencyErrors.PreconditionFailed);
        }

        return Result.Success(aggregate);
    }
}
