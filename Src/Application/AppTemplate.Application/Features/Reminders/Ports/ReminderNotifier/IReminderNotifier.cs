namespace AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

public interface IReminderNotifier
{
    Task NotifyAsync(ReminderNotification notification, CancellationToken cancellationToken = default);
}
