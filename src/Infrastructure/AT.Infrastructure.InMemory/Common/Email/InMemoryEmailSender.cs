using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.InMemory.Common.Email;

internal sealed class InMemoryEmailSender(RecordedEmails recorded, TimeProvider timeProvider) : IEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorded.Record(new SentEmail(recipient, subject, htmlBody, timeProvider.GetUtcNow()));

        return Task.CompletedTask;
    }
}
