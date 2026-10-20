using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.TodoLists.Model.Events;

namespace AppTemplate.Application.Features.Reminders.Services.Consumers.TodoItemCompleted;

internal sealed class CancelRemindersOnTodoItemCompletedConsumer(
    IReminderRepository reminders,
    IUnitOfWork unitOfWork) : IDomainEventConsumer<TodoItemCompletedDomainEvent>
{
    public async Task ConsumeAsync(
        TodoItemCompletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var candidates = await reminders.GetForTodoItemAsync(domainEvent.TodoItemId, cancellationToken);
        var pending = candidates.Where(reminder => reminder.State == ReminderState.Pending).ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        foreach (var reminder in pending)
        {
            reminder.Cancel();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
