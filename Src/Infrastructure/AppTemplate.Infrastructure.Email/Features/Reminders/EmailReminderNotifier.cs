using System.Globalization;
using AppTemplate.Application.Common.Localization;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Email.Features.Reminders;

/// <summary>
/// Rings a reminder by email. Resolving the owner's address belongs here rather than in the use
/// case: what "notify" means is an adapter's decision, and a use case that carried an address
/// would have picked email on the caller's behalf.
/// <para>
/// The subject and the body both come from <see cref="ReminderEmailTemplate"/>, in
/// <see cref="CurrentLanguage.Current"/> — which in <c>AppTemplate.Worker</c> is the language
/// <c>Localization:DefaultCulture</c> names, set once at start-up. A background pass has no request
/// to read a reader's own language from, so this mail is written in the deployment's default until
/// an account carries a stored preference; <c>docs/CONFIGURATION.md</c> says where that plugs in.
/// </para>
/// </summary>
internal sealed class EmailReminderNotifier(
    IUserProfilesService profiles,
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

        var mail = ReminderEmailTemplate.Create(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = profile.UserName,
                // Round-trip, not a culture-specific long date: this process builds with
                // InvariantGlobalization, so there is no French date format to reach for. A reader
                // gets an unambiguous instant rather than one formatted as if for somebody else.
                ["DueAt"] = notification.DueAt.ToString("u", CultureInfo.InvariantCulture),
            });

        await emailSender.SendAsync(profile.Email, mail.Subject, mail.Body, cancellationToken);
    }
}
