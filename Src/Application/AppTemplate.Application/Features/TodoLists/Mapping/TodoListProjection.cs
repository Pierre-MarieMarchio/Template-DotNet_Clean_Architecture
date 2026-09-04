using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Application.Features.TodoLists.Mapping;

/// <summary>
/// Turns the aggregate a command just wrote into the same DTOs a read would have produced,
/// without a second query.
/// </summary>
/// <remarks>
/// Sound only because <c>TodoListTracker</c> writes the store-assigned version and the refreshed
/// audit timestamps back onto the aggregate inside <c>SaveChangesAsync</c> — so once that call
/// has returned, <see cref="TodoList.Version"/>, <see cref="TodoList.CreatedAt"/> and
/// <see cref="TodoList.LastModifiedAt"/> already hold the values the row was just committed
/// with. Reading them back through <c>ITodoListQueries</c> instead would run a second, unrelated
/// query outside the transaction, with a window in which another writer commits and makes that
/// second read describe a version newer than the one this call produced.
/// <para>
/// Only correct because nothing between the write and this projection mutates the aggregate
/// again — in particular, a domain-event consumer running inside <c>SaveChangesAsync</c> must
/// not reach back into the aggregate it was raised from.
/// </para>
/// </remarks>
internal static class TodoListProjection
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
