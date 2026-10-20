using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.TodoLists.Model.Errors;

public static class TodoListErrors
{
    public static Error ListNotFound(Guid todoListId) => Error.NotFound(
        "todoList.notFound",
        $"No to-do list with id '{todoListId}' was found.");

    public static Error ItemNotFound(Guid todoItemId) => Error.NotFound(
        "todoItem.notFound",
        $"No to-do item with id '{todoItemId}' was found on this list.");
}
