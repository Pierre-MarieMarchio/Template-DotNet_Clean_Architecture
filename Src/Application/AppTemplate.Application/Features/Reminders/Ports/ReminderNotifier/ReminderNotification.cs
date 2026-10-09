using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

/// <summary>Everything an adapter needs to ring a reminder, and nothing about how it does it.</summary>
/// <param name="Recipient">
/// The owner's name and address, resolved just before the call. Which of them an adapter uses is its
/// own choice: an email notifier reads the address, a push notifier would read only
/// <paramref name="OwnerId"/>.
/// </param>
public sealed record ReminderNotification(
    UserId OwnerId,
    ReminderRecipient Recipient,
    Guid TodoItemId,
    DateTimeOffset DueAt);
