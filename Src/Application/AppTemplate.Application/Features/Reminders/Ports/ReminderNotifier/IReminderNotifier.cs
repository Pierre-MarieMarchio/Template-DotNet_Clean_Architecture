namespace AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

/// <summary>
/// Delivers a reminder's notification. What "ringing" means — an email, a push, a phone call — is
/// an adapter decision, not a use case one, so this names only the facts a message needs, never
/// <c>IEmailSender</c> directly.
/// </summary>
public interface IReminderNotifier
{
    Task NotifyAsync(ReminderNotification notification, CancellationToken cancellationToken = default);
}
