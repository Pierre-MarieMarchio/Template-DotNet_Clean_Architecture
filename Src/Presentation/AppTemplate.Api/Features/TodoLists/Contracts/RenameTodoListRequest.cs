namespace AppTemplate.Api.Features.TodoLists.Contracts;

/// <summary>Carries no list id: that travels in the route, so the two cannot disagree.</summary>
public sealed record RenameTodoListRequest(string Name);
