using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using AppTemplate.Domain.Features.TodoLists.Events;

namespace AppTemplate.Application.Features.Reminders.Consumers.TodoItemCompleted;

/// <summary>
/// Cancels every still-pending reminder on an item once it is completed, so a reminder does not
/// fire for something already done.
/// <para>
/// This runs after the transaction that completed the item has already committed — see
/// <c>DomainEventDispatchSaveChangesInterceptor</c> — so, unlike a use case, it owns committing
/// its own change through <see cref="IUnitOfWork"/> rather than riding one that already
/// happened. That commit runs on the same scoped <c>DbContext</c> the completion just used, one
/// save nested inside the other's post-commit callback; it is not a second, independent
/// transaction.
/// </para>
/// <para>
/// <b>This is a fast path, not the correctness guarantee — deleting this class would not make
/// firing wrong, only slower to notice.</b> <c>FireDueRemindersUseCase</c> re-checks completion
/// at firing time regardless, for exactly the case this consumer cannot cover: the event never
/// reaches it, or this call throws and is logged rather than retried (see
/// <c>DomainEventDispatcher</c>). Without this consumer a completed item's reminder would simply
/// wait until it next comes due to be cancelled there instead. A missed cancellation shows up as
/// <see cref="IReminderDiagnostics.RecordMissedCancellation"/>, not as a wrong notification —
/// which is what makes it safe for this consumer to be best-effort in the first place.
/// </para>
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
