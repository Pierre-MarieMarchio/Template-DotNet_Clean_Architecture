namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>Carries neither the list id nor the item id: both travel in the route.</summary>
/// <param name="Tag">Normalised to lower case by the domain.</param>
public sealed record AddTodoItemTagRequest(string Tag);
