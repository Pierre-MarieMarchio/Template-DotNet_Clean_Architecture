using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Domain.Features.TodoLists.Events;

/// <summary>Carries values, never the aggregate, so a handler cannot mutate the model after
/// the transaction has committed.</summary>
public sealed record TodoListCreatedDomainEvent(
    Guid TodoListId,
    Guid OwnerId,
    string Name,
    DateTimeOffset OccurredOn) : IDomainEvent;
