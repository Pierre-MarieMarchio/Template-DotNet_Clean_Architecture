using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Domain.Features.Reminders.Model.Events;

public sealed record ReminderFiredDomainEvent(
    Guid ReminderId,
    UserId OwnerId,
    Guid TodoItemId,
    DateTimeOffset OccurredOn) : IDomainEvent;
