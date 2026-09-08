using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Domain.Features.TodoLists.Events;

/// <summary>Carries values, never the aggregate, so a handler cannot mutate the model after
/// the transaction has committed.</summary>
public sealed record TodoListCreatedDomainEvent(
    Guid TodoListId,
    UserId OwnerId,
    string Name,
    DateTimeOffset OccurredOn) : IDomainEvent;
