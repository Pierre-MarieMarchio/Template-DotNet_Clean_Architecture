using AppTemplate.Application.Core.Common.Ports;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Smtp;

/// <summary>The <see cref="IEmailSender"/> port over MailKit, rather than the obsolete
/// <c>System.Net.Mail.SmtpClient</c>. Internal and sealed: an adapter for a port the application
/// layer declares; substituting it is composing a different module, not subclassing this one.
/// Sending is one connection per message — correct, and adequate for account emails at this volume;
/// bulk sending wants a different implementation of the same port, not a flag on this one.</summary>
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
