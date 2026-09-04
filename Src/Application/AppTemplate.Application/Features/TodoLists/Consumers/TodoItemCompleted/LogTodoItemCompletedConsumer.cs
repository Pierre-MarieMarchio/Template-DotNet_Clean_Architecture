using AppTemplate.Application.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Events;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.TodoLists.Consumers.TodoItemCompleted;

/// <summary>
/// Worked example of a domain-event consumer: complete an item and this runs, once the transaction
/// has committed. Replace it with something that matters — a notification, a projection refresh —
/// or delete it.
/// </summary>
/// <remarks>
/// It sits in the Application layer on purpose. Publishing events is a persistence mechanism, but
/// deciding what happens next is application behaviour, so a consumer needs nothing from any
/// infrastructure module and can reach this layer's own ports.
/// </remarks>
internal sealed class LogTodoItemCompletedConsumer(ILogger<LogTodoItemCompletedConsumer> logger)
    : IDomainEventConsumer<TodoItemCompletedDomainEvent>
{
    public Task ConsumeAsync(
        TodoItemCompletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Arguments are formatted before the call is entered, so the guard avoids doing that work
        // and discarding it when information logging is off.
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Item {TodoItemId} ({Title}) on list {TodoListId} was completed at {OccurredOn}.",
                domainEvent.TodoItemId,
                domainEvent.Title,
                domainEvent.TodoListId,
                domainEvent.OccurredOn);
        }

        return Task.CompletedTask;
    }
}
