namespace AppTemplate.Api.Features.TodoLists.Contracts.Requests;

/// <summary>
/// Deliberately not shared with <see cref="RenameTodoListRequest"/>: creating and renaming are two
/// operations whose bodies happen to agree today and will diverge on their own schedules.
/// </summary>
public sealed record CreateTodoListRequest(string Name);
