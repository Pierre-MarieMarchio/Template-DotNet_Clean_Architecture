namespace AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;

/// <summary>The owner as a notification greets and reaches them.</summary>
public sealed record ReminderRecipient(string Name, string Email);
