using AppTemplate.Domain.Core.Common.Events;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

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
/// <para>
/// <b>Which save publishes them.</b> Only a save of the context the source's rows are staged on: the
/// events describe that write, and another context's commit — the identity module's, or the
/// idempotency store's second instance of the same context — says nothing about whether it landed.
/// </para>
/// </summary>
public interface IDomainEventSource
{
    /// <summary>
    /// Whether <paramref name="context"/> is the context the writes behind these events are staged
    /// on, and so the one whose save may drain and publish them.
    /// </summary>
    /// <param name="context">The context being saved.</param>
    bool IsStagedOn(DbContext context);

    /// <summary>
    /// Returns the events raised so far and clears them, in one step.
    /// <para>
    /// Draining rather than reading is what stops an event being published twice: one that was
    /// taken cannot be taken again by a later save of the same aggregate in the same request, which
    /// is how a second <c>SaveChangesAsync</c> would otherwise re-publish everything the first did.
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
