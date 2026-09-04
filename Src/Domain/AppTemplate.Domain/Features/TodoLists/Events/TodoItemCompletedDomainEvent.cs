using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Domain.Features.TodoLists.Events;

/// <summary>Raised by the list, not the item, because the list is the consistency boundary —
/// so the event has to identify both.</summary>
public sealed record TodoItemCompletedDomainEvent(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    DateTimeOffset OccurredOn) : IDomainEvent;
