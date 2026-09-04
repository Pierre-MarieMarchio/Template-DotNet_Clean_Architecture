namespace AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

/// <summary>Everything an adapter needs to ring a reminder, and nothing about how it does it.</summary>
public sealed record ReminderNotification(Guid OwnerId, Guid TodoItemId, DateTimeOffset DueAt);
