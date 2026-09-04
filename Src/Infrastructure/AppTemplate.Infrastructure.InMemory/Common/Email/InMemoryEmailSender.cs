using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Infrastructure.InMemory.Common.Email;

/// <summary>
/// An <see cref="IEmailSender"/> that delivers to memory and never opens a socket.
/// <para>
/// Internal and sealed, like every other adapter: the observable surface is
/// <see cref="RecordedEmails"/>, not this class. A test that named the sender would be
/// asserting on the double rather than on the behaviour it stands in for.
/// </para>
/// <para>
/// It does not throw, does not queue, and does not pretend to fail: a double that simulates
/// failure modes accumulates a second implementation of the thing under test. A test needing
/// a failing sender substitutes one for the single call it cares about.
/// </para>
/// </summary>
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
