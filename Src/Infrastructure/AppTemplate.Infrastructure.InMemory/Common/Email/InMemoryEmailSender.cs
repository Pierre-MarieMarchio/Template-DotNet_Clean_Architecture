using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Infrastructure.InMemory.Common.Email;

internal sealed class InMemoryEmailSender(RecordedEmails recorded, IDateTimeProvider dateTimeProvider) : IEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorded.Record(new SentEmail(recipient, subject, htmlBody, dateTimeProvider.UtcNow));

        return Task.CompletedTask;
    }
}
