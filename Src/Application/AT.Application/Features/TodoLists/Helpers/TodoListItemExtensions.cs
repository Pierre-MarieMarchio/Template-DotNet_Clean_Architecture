using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;

namespace AppTemplate.Application.Features.TodoLists.Helpers;

public static class TodoListItemExtensions
{
    public static Result RequireItem(this TodoList todoList, Guid todoItemId)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        return todoList.Items.Any(item => item.Id == todoItemId)
            ? Result.Success()
            : Result.Failure(TodoListErrors.ItemNotFound(todoItemId));
    }
}
