using AppTemplate.Application.Common;

namespace AppTemplate.Application.Features.TodoLists.Errors;

public static class TodoListErrors
{
    /// <summary>
    /// Also returned when the list belongs to somebody else: distinguishing the two would let a
    /// caller enumerate other users' list ids by comparing 403 against 404.
    /// </summary>
    public static Error ListNotFound(Guid todoListId) => Error.NotFound(
        "todoList.notFound",
        $"No to-do list with id '{todoListId}' was found.");

    public static Error ItemNotFound(Guid todoItemId) => Error.NotFound(
        "todoItem.notFound",
        $"No to-do item with id '{todoItemId}' was found on this list.");
}
