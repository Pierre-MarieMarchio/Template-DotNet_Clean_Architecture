using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Domain.Features.Reminders.Events;

/// <summary>
/// Raised once a reminder's notification has actually gone out, never before: a consumer of this
/// event is entitled to assume the owner has been told.
/// </summary>
public sealed record ReminderFiredDomainEvent(
    Guid ReminderId,
    Guid OwnerId,
    Guid TodoItemId,
    DateTimeOffset OccurredOn) : IDomainEvent;
