namespace AppTemplate.Infrastructure.InMemory.Reminders;

/// <summary>One reminder notification delivered, exactly as the port received it.</summary>
/// <param name="OwnerId">Whose reminder this was.</param>
/// <param name="TodoItemId">The item it was about.</param>
/// <param name="DueAt">The instant it was due at.</param>
/// <param name="SentAt">The instant the delivery happened, taken from the injected clock.</param>
public sealed record SentReminderNotification(Guid OwnerId, Guid TodoItemId, DateTimeOffset DueAt, DateTimeOffset SentAt);
