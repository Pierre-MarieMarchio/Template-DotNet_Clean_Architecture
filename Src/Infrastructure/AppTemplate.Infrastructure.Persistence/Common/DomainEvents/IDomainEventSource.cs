using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Infrastructure.Persistence.Common.DomainEvents;

/// <summary>
/// Somewhere the events raised during the current request have accumulated, ready to be published
/// once the transaction commits.
/// <para>
/// EF does not map the domain types, so no aggregate is in the change tracker and an interceptor that
/// looked there would find nothing, publish nothing and fail silently — the worst possible failure for
/// a mechanism whose whole job is a side effect. The feature that holds the aggregates is asked
/// instead: <c>Common/</c> knows only this interface, and each feature's aggregate tracker implements
/// it.
/// </para>
/// </summary>
internal interface IDomainEventSource
{
    /// <summary>
    /// Returns the events raised so far and clears them, in one step.
    /// <para>
    /// Draining rather than reading is what makes delivery exactly-once: an event that was taken
    /// cannot be taken again by a later save of the same aggregate in the same request, which is how
    /// a second <c>SaveChangesAsync</c> would otherwise re-publish everything the first one did.
    /// </para>
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DrainDomainEvents();

    /// <summary>
    /// Takes back events that were drained but never published, so that the next drain returns them
    /// ahead of anything raised since.
    /// <para>
    /// A failed save commits nothing, and the documented recovery for a conflict is to reload and save
    /// again. The events have already been taken out of the aggregates by then and cannot be raised a
    /// second time, so discarding them would leave the retry publishing nothing at all.
    /// </para>
    /// </summary>
    void Restore(IEnumerable<IDomainEvent> domainEvents);
}
