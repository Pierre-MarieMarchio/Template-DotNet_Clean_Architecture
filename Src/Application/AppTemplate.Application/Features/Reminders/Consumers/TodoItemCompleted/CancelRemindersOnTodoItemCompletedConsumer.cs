using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using AppTemplate.Domain.Features.TodoLists.Events;

namespace AppTemplate.Application.Features.Reminders.Consumers.TodoItemCompleted;

/// <summary>
/// Cancels every still-pending reminder on an item once it is completed, so a reminder does not
/// fire for something already done. Runs after the transaction that completed the item has
/// committed — see <c>DomainEventDispatchSaveChangesInterceptor</c> — so, unlike a use case, it
/// owns committing its own change through <see cref="IUnitOfWork"/>, on the same scoped
/// <c>DbContext</c> the completion just used. <b>A fast path, not the correctness guarantee:</b>
/// <c>FireDueRemindersUseCase</c> re-checks completion at firing time, for exactly the case this
/// consumer cannot cover — the event never reaches it, or this call throws and is not retried —
/// and a missed cancellation shows up as <see cref="IReminderDiagnostics.RecordMissedCancellation"/>,
/// not as a wrong notification.
/// </summary>
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
            // Also what a redelivery of this event finds: Cancel() is idempotent by shape, but
            // there is no point loading and saving nothing to prove it.
            return;
        }

        foreach (var reminder in pending)
        {
            reminder.Cancel();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
