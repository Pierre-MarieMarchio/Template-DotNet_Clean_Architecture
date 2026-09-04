using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Email.Services;

/// <summary>
/// Rings a reminder by email. Resolving the owner's address belongs here rather than in the use
/// case: what "notify" means is an adapter's decision, and a use case that carried an address
/// would have picked email on the caller's behalf.
/// </summary>
internal sealed class EmailReminderNotifier(
    IUserProfiles profiles,
    IEmailSender emailSender,
    ILogger<EmailReminderNotifier> logger) : IReminderNotifier
{
    public async Task NotifyAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var profile = await profiles.FindByIdAsync(notification.OwnerId, cancellationToken);

        if (profile is null)
        {
            // The account went away between scheduling and firing. Nothing to deliver to, and
            // nothing wrong: the caller retires the reminder either way.
            logger.LogWarning(
                "Reminder for item {TodoItemId} has no account to notify.",
                notification.TodoItemId);

            return;
        }

        await emailSender.SendAsync(
            profile.Email,
            "Reminder",
            $"<p>A to-do item you asked to be reminded about was due at "
            + $"{notification.DueAt:u}.</p>",
            cancellationToken);
    }
}
