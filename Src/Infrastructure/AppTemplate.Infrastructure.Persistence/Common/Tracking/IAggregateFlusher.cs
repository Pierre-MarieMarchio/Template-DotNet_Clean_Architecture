using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.Tracking;

/// <summary>
/// The bridge between a domain aggregate and the persistence model that stores it, for one feature.
/// <para>
/// This abstraction exists because EF Core does not map the domain types. That is a deliberate
/// choice, and it has a direct cost: EF's change tracker cannot see a mutation made inside an
/// aggregate, because it is not tracking the aggregate. Something has to write the aggregate's state
/// onto the tracked rows before EF computes its diff, and something has to read the store's own
/// values — the concurrency token, the audit stamps — back into the aggregate afterwards. That
/// something is a flusher, one per aggregate, and this is the only shape <c>Common/</c> knows about.
/// </para>
/// <para>
/// The two halves must not be collapsed into "rebuild the row from the aggregate". Assigning every
/// column from a freshly mapped, detached record makes every write a full-row <c>UPDATE</c> and
/// overwrites the audit columns the interceptor owns — precisely the defect this template was
/// rescued from. An implementation therefore mutates the <em>tracked</em> rows in place and lets EF
/// decide what actually changed.
/// </para>
/// </summary>
internal interface IAggregateFlusher
{
    /// <summary>
    /// Writes the state of every aggregate this flusher is holding onto its tracked rows, reconciles
    /// child collections, and marks a root modified when only a child changed.
    /// <para>
    /// Called from a save-changes interceptor registered <b>first</b>, so that everything downstream —
    /// audit stamping, event collection, EF's own diff — sees the finished picture.
    /// </para>
    /// </summary>
    /// <param name="context">The context being saved. Passed in rather than injected, so that a
    /// flusher can be constructed by the same interceptor the context's options resolve, without the
    /// two depending on each other.</param>
    void FlushTo(DbContext context);

    /// <summary>
    /// Copies the values the store decided — the concurrency token, the audit stamps — back into the
    /// aggregates, after a successful save. Without this the aggregate a use case is still holding
    /// would carry a stale version, and its next write in the same request would fail against a
    /// token it had itself just moved.
    /// </summary>
    void RefreshFromStore();
}
