using AppTemplate.Application.Common.Ports;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Smtp;

/// <summary>
/// The <see cref="IEmailSender"/> port, over MailKit, rather than <c>System.Net.Mail.SmtpClient</c>
/// (obsolete since .NET 5) — a type that invites being constructed anew per email, never disposed,
/// and configured through null-forgiving lookups when nothing declares the settings' shape.
/// <para>
/// Internal and sealed. It is an adapter for a port the application layer declares; callers
/// depend on <see cref="IEmailSender"/> and nothing outside this assembly has any reason to
/// name this type. Substituting it is a matter of composing a different module — see
/// <c>AppTemplate.Infrastructure.InMemory</c> — not of subclassing this one.
/// </para>
/// <para>
/// Sending is one connection per message: correct, and adequate for account emails at this
/// volume. A system sending in bulk wants a pooled or queued sender, which is a different
/// implementation of the same port rather than a flag on this one.
/// </para>
/// </summary>
internal sealed class MailKitEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
