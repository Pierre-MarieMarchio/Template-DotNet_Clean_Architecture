using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Common.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;

/// <summary>
/// The change tracker EF cannot be, for one aggregate, for the duration of one request.
/// <para>
/// EF's change tracker does three things this layer still needs and can no longer get from it, because
/// it is not tracking the domain types. It is an <b>identity map</b> — ask for the same aggregate twice
/// and get the same object, so two use cases in one request cannot each hold a divergent copy. It is
/// the <b>list of things to write</b> when a save happens. And it is where the <b>domain events</b>
/// raised during the request can be found, since the aggregates that raised them are reachable from
/// nowhere else.
/// </para>
/// <para>
/// It is scoped to the request, like the context whose rows it points at, and it is a separate object
/// from the repository on purpose: the flush interceptor has to reach it while the context's options
/// are being built, and a tracker that depended on the context would close that loop into a cycle.
/// </para>
/// </summary>
internal interface ITodoListTracker : IAggregateFlusher, IDomainEventSource
{
    /// <summary>
    /// The live aggregate already loaded in this request under <paramref name="id"/>, or <c>null</c>
    /// when there is none — including when it has been staged for deletion.
    /// <para>
    /// Consulting this before querying is what keeps the identity map honest. Without it, a second
    /// <c>GetAsync</c> would build a second aggregate from the same tracked row, both would be flushed
    /// onto it, and whichever was flushed last would win — the same class of defect as two
    /// <c>DbContext</c> instances in one request.
    /// </para>
    /// </summary>
    TodoList? Find(Guid id);

    /// <summary>Records the pairing of an aggregate with the row that stores it.</summary>
    void Track(TodoList aggregate, TodoListRecord record);

    /// <summary>The row an aggregate is stored in, or <c>null</c> when it is not tracked here.</summary>
    TodoListRecord? FindRecord(Guid id);

    /// <summary>
    /// Notes that an aggregate's row has been staged for deletion: nothing more is written to it, and
    /// <see cref="Find"/> stops returning it.
    /// <para>
    /// It is not forgotten, though. Its pending domain events are still drained on the next save,
    /// because a deletion is something that happened and an event raised on the way out would otherwise
    /// be undeliverable. That is also why the aggregate is passed rather than its id: one that was never
    /// loaded in this request has to be taken in here, or it would never be drained at all.
    /// </para>
    /// </summary>
    void MarkRemoved(TodoList aggregate, TodoListRecord record);
}
