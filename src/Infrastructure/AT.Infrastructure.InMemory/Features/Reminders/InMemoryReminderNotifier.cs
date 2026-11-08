using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

namespace AppTemplate.Infrastructure.InMemory.Features.Reminders;

internal sealed class InMemoryReminderNotifier(
    RecordedReminderNotifications recorded,
    TimeProvider timeProvider) : IReminderNotifier
{
    public Task NotifyAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        recorded.Record(new SentReminderNotification(
            notification.OwnerId,
            notification.TodoItemId,
            notification.DueAt,
            timeProvider.GetUtcNow()));

        return Task.CompletedTask;
    }
}
