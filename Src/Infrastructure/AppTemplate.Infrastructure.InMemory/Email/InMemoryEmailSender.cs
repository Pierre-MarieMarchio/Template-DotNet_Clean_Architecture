using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Infrastructure.InMemory.Email;

/// <summary>One delivered message, exactly as the port received it.</summary>
/// <param name="Recipient">The destination address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="HtmlBody">The rendered body, so a test can assert on a confirmation link
/// instead of on the fact that "an email was sent".</param>
/// <param name="SentAt">The instant the send happened, taken from the injected clock.</param>
public sealed record SentEmail(string Recipient, string Subject, string HtmlBody, DateTimeOffset SentAt);

/// <summary>
/// The mailbox <see cref="InMemoryEmailSender"/> writes to. Public and resolvable, because
/// recording is only useful if something can read it back: a test asks this for the last
/// message to an address and pulls the confirmation token out of the body.
/// <para>
/// A singleton for the life of the host, and internally locked, because an integration test
/// sends from several requests at once.
/// </para>
/// </summary>
public sealed class RecordedEmails
{
    private readonly object _gate = new();
    private readonly List<SentEmail> _sent = [];

    /// <summary>Every message sent so far, oldest first, as a snapshot that will not change
    /// underneath the caller.</summary>
    public IReadOnlyList<SentEmail> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sent];
        }
    }

    /// <summary>
    /// The most recent message to an address, or <c>null</c> if there is none. Address
    /// comparison is ordinal-ignore-case, which is how the identity store normalises them.
    /// </summary>
    public SentEmail? LastTo(string recipient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        lock (_gate)
        {
            for (int index = _sent.Count - 1; index >= 0; index--)
            {
                if (string.Equals(_sent[index].Recipient, recipient, StringComparison.OrdinalIgnoreCase))
                {
                    return _sent[index];
                }
            }

            return null;
        }
    }

    /// <summary>Empties the mailbox, so one test's messages cannot be read by the next.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _sent.Clear();
        }
    }

    internal void Record(SentEmail email)
    {
        lock (_gate)
        {
            _sent.Add(email);
        }
    }
}

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
