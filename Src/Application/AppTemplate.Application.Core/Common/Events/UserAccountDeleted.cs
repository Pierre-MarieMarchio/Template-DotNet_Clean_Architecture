using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Events;

/// <summary>
/// An account is gone: the id named no fewer of this application's own rows from the moment the
/// fact was published. Raised by no aggregate — the account is not one — but by the operation that
/// deleted it, once that deletion has committed, which is the same contract
/// <see cref="IDomainEventConsumer{TEvent}"/> holds for every event: a consumer runs after the
/// transaction that made the fact true.
/// <para>
/// Each feature that owns user data decides what the fact means for its own rows — delete them,
/// anonymise them — and applies that policy in a consumer of this event, idempotently, re-deriving
/// from the fact rather than trusting delivery: see <c>IDeletedAccountDataPolicy</c>. A periodic
/// sweep is the guarantee behind the event, which is only a fast path.
/// </para>
/// <para>
/// In-process and not durable: nothing retries a consumer that was never reached. The sweep is
/// what catches those; a project that needs the fact itself to survive a crash publishes it from
/// an outbox instead, and keeps this consumer unchanged.
/// </para>
/// </summary>
public sealed record UserAccountDeleted(UserId UserId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>The fact's instant, under the name the event contract reads.</summary>
    public DateTimeOffset OccurredOn => OccurredAt;
}
