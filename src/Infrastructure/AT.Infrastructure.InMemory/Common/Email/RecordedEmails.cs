namespace AppTemplate.Infrastructure.InMemory.Common.Email;

public sealed class RecordedEmails
{
    private readonly object _gate = new();
    private readonly List<SentEmail> _sent = [];

    public IReadOnlyList<SentEmail> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sent];
        }
    }

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
