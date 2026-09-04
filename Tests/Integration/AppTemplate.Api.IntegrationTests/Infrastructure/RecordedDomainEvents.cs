using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The domain events a consumer actually received during a test. A singleton, because dispatch
/// happens inside a request scope and the assertion happens outside it.
/// </summary>
public sealed class RecordedDomainEvents
{
    private readonly object _gate = new();
    private readonly List<TodoItemCompletedDomainEvent> _completedItems = [];

    public IReadOnlyList<TodoItemCompletedDomainEvent> CompletedItems
    {
        get
        {
            lock (_gate)
            {
                return [.. _completedItems];
            }
        }
    }

    public void Record(TodoItemCompletedDomainEvent domainEvent)
    {
        lock (_gate)
        {
            _completedItems.Add(domainEvent);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _completedItems.Clear();
        }
    }
}

/// <summary>
/// A second consumer for an event the TodoLists module already consumes, registered through
/// <c>PersistenceModule.AddDomainEventConsumer</c> exactly as a module would. It proves two things
/// at once: that the dispatcher resolves every consumer of an event rather than the first, and that
/// a consumer registered by a host outside the owning module is reached.
/// </summary>
internal sealed class RecordingTodoItemCompletedConsumer(RecordedDomainEvents recorded)
    : IDomainEventConsumer<TodoItemCompletedDomainEvent>
{
    public Task ConsumeAsync(
        TodoItemCompletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        recorded.Record(domainEvent);

        return Task.CompletedTask;
    }
}
