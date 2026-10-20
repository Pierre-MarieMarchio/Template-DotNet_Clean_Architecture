using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Model.Events;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

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
