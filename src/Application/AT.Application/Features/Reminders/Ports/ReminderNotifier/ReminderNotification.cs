using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

public sealed record ReminderNotification(
    UserId OwnerId,
    ReminderRecipient Recipient,
    Guid TodoItemId,
    DateTimeOffset DueAt);
