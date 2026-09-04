using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Domain.Features.TodoLists.Events;

/// <summary>Mirrors <see cref="TodoItemCompletedDomainEvent"/>, so a consumer tracking
/// completions can also track reversals instead of drifting after the first reopen.</summary>
public sealed record TodoItemReopenedDomainEvent(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    DateTimeOffset OccurredOn) : IDomainEvent;
