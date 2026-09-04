namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;

public sealed record GetTodoItemQuery(Guid TodoListId, Guid TodoItemId);
