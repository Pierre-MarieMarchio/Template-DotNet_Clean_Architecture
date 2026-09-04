namespace AppTemplate.Infrastructure.InMemory.Email;

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
