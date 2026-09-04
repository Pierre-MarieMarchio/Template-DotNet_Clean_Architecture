namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>
/// The complete title/description representation of one item. Carries neither the list id nor the
/// item id: both travel in the route.
/// </summary>
/// <param name="Title">Must stay unique within its list, excluding the item itself.</param>
/// <param name="Description">Absent clears it: this is the whole representation, not a patch.</param>
public sealed record UpdateTodoItemRequest(string Title, string? Description);
