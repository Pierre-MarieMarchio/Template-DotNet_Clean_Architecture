namespace AppTemplate.Api.Features.TodoLists.Contracts;

/// <summary>Carries no list id: that travels in the route.</summary>
/// <param name="Title">Must be unique within its list.</param>
/// <param name="Tags">Normalised and de-duplicated by the domain.</param>
public sealed record AddTodoItemRequest(string Title, string? Description, IReadOnlyList<string>? Tags);
