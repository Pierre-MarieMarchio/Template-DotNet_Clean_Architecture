using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Domain.Features.TodoLists.Model.Events;

public sealed record TodoListCreatedDomainEvent(
    Guid TodoListId,
    UserId OwnerId,
    string Name,
    DateTimeOffset OccurredOn) : IDomainEvent;
