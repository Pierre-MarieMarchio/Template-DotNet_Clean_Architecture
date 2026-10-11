using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Application.Features.TodoLists.Mapping;

/// <summary>Turns the aggregate a command just wrote into the same DTOs a read would have produced, without a second query.</summary>
/// <remarks>Sound only because <c>TodoListTracker</c> writes the store-assigned version and the
/// refreshed audit timestamps back onto the aggregate inside <c>SaveChangesAsync</c>, so
/// <see cref="IVersioned.Version"/>, <see cref="IAuditable.CreatedAt"/> and
/// <see cref="IAuditable.LastModifiedAt"/> already hold the committed values once that call
/// returns; a second read through <c>ITodoListQueries</c> would run outside the transaction, where
/// another writer could commit first. Also only correct because nothing between the write and this
/// projection mutates the aggregate again — a domain-event consumer running inside
/// <c>SaveChangesAsync</c> must not reach back into the aggregate it was raised from.</remarks>
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

    // Matches TodoListQueries.GetDetailAsync's `Items.OrderBy(item => item.Title)` exactly: the
    // same list must read the same way whether it comes back from a write or from a query.
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
