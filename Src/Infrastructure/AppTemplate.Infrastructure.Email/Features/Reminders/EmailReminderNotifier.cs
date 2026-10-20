using System.Globalization;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Infrastructure.Core.Common.Templating;

namespace AppTemplate.Infrastructure.Email.Features.Reminders;

/// <summary>
/// Rings a reminder by email, at the address the use case resolved through
/// <c>IReminderRecipientDirectory</c>. Choosing email is this adapter's decision; the use case only
/// says who the owner is.
/// <para>
/// The subject and the body both come from the embedded <c>ReminderEmailTemplate</c>, in
/// <see cref="CurrentLanguage.Current"/> — which in a background job is the language
/// <c>Localization:DefaultCulture</c> names, set once at start-up. A background pass has no request
/// to read a reader's own language from, so this mail is written in the deployment's default until
/// an account carries a stored preference; <c>docs/CONFIGURATION.md</c> says where that plugs in.
/// </para>
/// </summary>
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
                // Round-trip, not a culture-specific long date: this process builds with
                // InvariantGlobalization, so there is no French date format to reach for. A reader
                // gets an unambiguous instant rather than one formatted as if for somebody else.
                ["DueAt"] = notification.DueAt.ToString("u", CultureInfo.InvariantCulture),
            });

        await emailSender.SendAsync(notification.Recipient.Email, mail.Subject, mail.Body, cancellationToken);
    }
}
