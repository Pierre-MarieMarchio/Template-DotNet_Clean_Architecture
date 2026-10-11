using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;

/// <summary>The change tracker EF cannot be, for one aggregate, for the duration of one request:
/// an <b>identity map</b> (the same aggregate twice is the same object, so two use cases cannot hold
/// divergent copies), the <b>list of things to write</b> when a save happens, and where the
/// <b>domain events</b> raised during the request are found. Scoped to the request, and separate
/// from the repository on purpose — the flush interceptor reaches it while the context's options are
/// being built, and a tracker that depended on the context would close that loop into a cycle.</summary>
internal interface ITodoListTracker : IAggregateFlusher, IDomainEventSource
{
    /// <summary>The live aggregate already loaded in this request under <paramref name="id"/>, or
    /// <c>null</c> when there is none — including when it has been staged for deletion. Consulting this
    /// before querying is what keeps the identity map honest: without it a second <c>GetAsync</c>
    /// would build a second aggregate from the same tracked row, and whichever was flushed last would
    /// win.</summary>
    TodoList? Find(Guid id);

    /// <summary>Records the pairing of an aggregate with the row that stores it.</summary>
    void Track(TodoList aggregate, TodoListRecord record);

    /// <summary>The row an aggregate is stored in, or <c>null</c> when it is not tracked here.</summary>
    TodoListRecord? FindRecord(Guid id);

    /// <summary>Notes that an aggregate's row has been staged for deletion: nothing more is written
    /// to it, and <see cref="Find"/> stops returning it. Its pending domain events are still drained on
    /// the next save — a deletion is something that happened — which is also why the aggregate is
    /// passed rather than its id: one never loaded in this request would never be drained at
    /// all.</summary>
    void MarkRemoved(TodoList aggregate, TodoListRecord record);
}
