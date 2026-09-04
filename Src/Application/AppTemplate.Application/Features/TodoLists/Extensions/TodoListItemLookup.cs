using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Application.Features.TodoLists.Extensions;

/// <summary>
/// Existence, and nothing else: a use case that needs the item itself still reads it from
/// <see cref="TodoList.Items"/>, and a use case that mutates it still goes through the
/// aggregate's own methods. This only turns an unknown id into the same 404 every command uses.
/// </summary>
public static class TodoListItemLookup
{
    public static Result RequireItem(this TodoList todoList, Guid todoItemId)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        return todoList.Items.Any(item => item.Id == todoItemId)
            ? Result.Success()
            : Result.Failure(TodoListErrors.ItemNotFound(todoItemId));
    }
}
