namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>Carries neither the list id nor the item id: both travel in the route.</summary>
/// <param name="Tags">
/// The complete set the item should end up with; anything not in it is removed, and an empty list
/// clears them all.
/// </param>
public sealed record ReplaceTodoItemTagsRequest(IReadOnlyList<string> Tags);
