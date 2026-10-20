namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>The name of a new todo list.</summary>
public sealed record CreateTodoListRequest(string Name);
