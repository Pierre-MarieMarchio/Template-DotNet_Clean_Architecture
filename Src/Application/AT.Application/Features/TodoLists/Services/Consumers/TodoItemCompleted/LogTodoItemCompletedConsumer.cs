using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.TodoLists.Services.Consumers.TodoItemCompleted;

internal sealed class LogTodoItemCompletedConsumer(ILogger<LogTodoItemCompletedConsumer> logger)
    : IDomainEventConsumer<TodoItemCompletedDomainEvent>
{
    public Task ConsumeAsync(
        TodoItemCompletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

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
