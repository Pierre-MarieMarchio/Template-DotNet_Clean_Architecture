using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

namespace AppTemplate.Infrastructure.InMemory.Features.Reminders;

internal sealed class InMemoryReminderNotifier(
    RecordedReminderNotifications recorded,
    IDateTimeProvider dateTimeProvider) : IReminderNotifier
{
    public Task NotifyAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        recorded.Record(new SentReminderNotification(
            notification.OwnerId,
            notification.TodoItemId,
            notification.DueAt,
            dateTimeProvider.UtcNow));

        return Task.CompletedTask;
    }
}
