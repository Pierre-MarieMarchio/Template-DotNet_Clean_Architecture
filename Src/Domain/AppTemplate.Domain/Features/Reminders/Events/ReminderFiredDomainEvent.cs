using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Domain.Features.Reminders.Events;

/// <summary>
/// Raised once a reminder's notification has actually gone out, never before: a consumer of this
/// event is entitled to assume the owner has been told.
/// </summary>
public sealed record ReminderFiredDomainEvent(
    Guid ReminderId,
    UserId OwnerId,
    Guid TodoItemId,
    DateTimeOffset OccurredOn) : IDomainEvent;
