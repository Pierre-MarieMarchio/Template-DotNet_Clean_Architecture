using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Events;

/// <summary>
/// An account is gone: the id names no fewer of this application's own rows from the moment the
/// fact was published. Raised by no aggregate — the account is not one — but by the operation that
/// deleted it, once that deletion has committed, the contract every event holds: a consumer runs
/// after the transaction that made the fact true. Each feature that owns user data decides what
/// the fact means for its own rows and applies that policy idempotently, re-deriving from the fact
/// rather than trusting delivery — see <c>IDeletedAccountDataPolicy</c>; a periodic sweep is the
/// guarantee behind the event, which is only a fast path. In-process and not durable: nothing
/// retries a consumer that was never reached, and the sweep is what catches those.
/// </summary>
public sealed record UserAccountDeleted(UserId UserId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>The fact's instant, under the name the event contract reads.</summary>
    public DateTimeOffset OccurredOn => OccurredAt;
}
