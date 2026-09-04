namespace AppTemplate.Infrastructure.InMemory.Reminders;

/// <summary>
/// The mailbox <see cref="InMemoryReminderNotifier"/> writes to. Public and resolvable, the same
/// shape as <c>RecordedEmails</c>: recording is only useful if something can read it back, and a
/// test asks this for the last notification to an owner.
/// <para>
/// A singleton for the life of the host, and internally locked, because an integration test or a
/// running worker delivers from several notifications at once.
/// </para>
/// </summary>
public sealed class RecordedReminderNotifications
{
    private readonly object _gate = new();
    private readonly List<SentReminderNotification> _sent = [];

    /// <summary>Every notification delivered so far, oldest first, as a snapshot that will not
    /// change underneath the caller.</summary>
    public IReadOnlyList<SentReminderNotification> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sent];
        }
    }

    /// <summary>The most recent notification for a to-do item, or <c>null</c> if there is none.</summary>
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

    /// <summary>Empties the mailbox, so one test's notifications cannot be read by the next.</summary>
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
