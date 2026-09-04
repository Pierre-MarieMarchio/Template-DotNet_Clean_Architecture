namespace AppTemplate.Domain.Features.Reminders.ValueObjects;

/// <summary>Where a reminder is in its one-way life.</summary>
public enum ReminderState
{
    /// <summary>Scheduled, and still expected to fire.</summary>
    Pending,

    /// <summary>Notified. Terminal: a fired reminder is never eligible again.</summary>
    Fired,

    /// <summary>Called off before it fired. Terminal.</summary>
    Cancelled,
}
