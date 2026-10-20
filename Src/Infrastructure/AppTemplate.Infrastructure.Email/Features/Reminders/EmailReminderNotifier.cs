using System.Globalization;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Infrastructure.Core.Common.Templating;

namespace AppTemplate.Infrastructure.Email.Features.Reminders;

internal sealed class EmailReminderNotifier(IEmailSender emailSender) : IReminderNotifier
{
    private static readonly EmailTemplate _template = new(
        typeof(EmailReminderNotifier).Assembly,
        "ReminderEmailTemplate");

    public async Task NotifyAsync(
        ReminderNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var mail = _template.Render(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RecipientName"] = notification.Recipient.Name,
                ["DueAt"] = notification.DueAt.ToString("u", CultureInfo.InvariantCulture),
            });

        await emailSender.SendAsync(notification.Recipient.Email, mail.Subject, mail.Body, cancellationToken);
    }
}
