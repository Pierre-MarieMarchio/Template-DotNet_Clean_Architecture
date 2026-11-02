using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;

namespace AppTemplate.Application.Features.TodoLists.Model.Mapping;

internal static class TodoListDtoMapping
{
    public static Versioned<TodoListDetailDto> Detail(TodoList todoList)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        var detail = new TodoListDetailDto(
            todoList.Id,
            todoList.Name.Value,
            todoList.CreatedAt,
            todoList.LastModifiedAt,
            [.. Ordered(todoList).Select(ToDto)]);

        return new Versioned<TodoListDetailDto>(detail, todoList.Version);
    }

    public static Versioned<TodoItemDto> Item(TodoList todoList, Guid itemId)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        var item = todoList.Items.First(candidate => candidate.Id == itemId);

        return new Versioned<TodoItemDto>(ToDto(item), todoList.Version);
    }

    private static IEnumerable<TodoItem> Ordered(TodoList todoList) =>
        todoList.Items.OrderBy(item => item.Title.Value, StringComparer.Ordinal);

    private static TodoItemDto ToDto(TodoItem item) => new(
        item.Id,
        item.Title.Value,
        item.Description,
        item.IsCompleted,
        item.CompletedAt,
        [.. item.Tags.Select(tag => tag.Value)]);
}
