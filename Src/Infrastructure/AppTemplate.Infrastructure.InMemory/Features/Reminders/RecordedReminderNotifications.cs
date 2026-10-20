namespace AppTemplate.Infrastructure.InMemory.Features.Reminders;

public sealed class RecordedReminderNotifications
{
    private readonly object _gate = new();
    private readonly List<SentReminderNotification> _sent = [];

    public IReadOnlyList<SentReminderNotification> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sent];
        }
    }

    public SentReminderNotification? LastFor(Guid todoItemId)
    {
        lock (_gate)
        {
            for (int index = _sent.Count - 1; index >= 0; index--)
            {
                if (_sent[index].TodoItemId == todoItemId)
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

    internal void Record(SentReminderNotification notification)
    {
        lock (_gate)
        {
            _sent.Add(notification);
        }
    }
}
