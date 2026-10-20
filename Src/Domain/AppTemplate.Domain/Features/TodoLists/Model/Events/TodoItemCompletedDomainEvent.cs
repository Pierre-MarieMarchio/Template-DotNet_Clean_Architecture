using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Domain.Features.TodoLists.Model.Events;

public sealed record TodoItemCompletedDomainEvent(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    DateTimeOffset OccurredOn) : IDomainEvent;
