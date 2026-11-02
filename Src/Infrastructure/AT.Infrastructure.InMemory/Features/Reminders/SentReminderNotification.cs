using AppTemplate.Domain.Core.Common.Primitives;
namespace AppTemplate.Infrastructure.InMemory.Features.Reminders;

public sealed record SentReminderNotification(UserId OwnerId, Guid TodoItemId, DateTimeOffset DueAt, DateTimeOffset SentAt);
