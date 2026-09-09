using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ownership;

/// <summary>
/// The gate a command loads an owned aggregate through: ownership first, then the version the
/// caller said it was deciding against.
/// </summary>
/// <remarks>
/// <para>
/// A non-owner is answered exactly as an absent aggregate. Telling the two apart would turn any
/// endpoint taking an id into a way of asking whether that id belongs to somebody.
/// </para>
/// <para>
/// Which error says "absent" is the feature's to decide, which is why one is passed in rather than
/// minted here: the policy stays with the feature and only the mechanism is shared.
/// </para>
/// </remarks>
public static class OwnedAggregate
{
    /// <summary>Checks the two things every command checks before it acts on a loaded aggregate.</summary>
    /// <typeparam name="TAggregate">The aggregate type, which knows its owner and its version.</typeparam>
    /// <param name="aggregate">What the repository answered, which may be nothing.</param>
    /// <param name="ownerId">The caller, already narrowed from <c>ICurrentUser</c>.</param>
    /// <param name="notFound">The feature's own error for an aggregate this caller may not see.</param>
    /// <param name="precondition">The versions the caller will accept, or null when it named none.</param>
    /// <returns>
    /// The aggregate; <paramref name="notFound"/> when it is absent or somebody else's; or
    /// <see cref="ConcurrencyErrors.PreconditionFailed"/> when ownership holds and the version does
    /// not.
    /// </returns>
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

        // Compared against the aggregate the caller just loaded, so nothing can commit between the
        // comparison and whatever the caller does with the result.
        if (precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version))
        {
            return Result.Failure<TAggregate>(ConcurrencyErrors.PreconditionFailed);
        }

        return Result.Success(aggregate);
    }
}
