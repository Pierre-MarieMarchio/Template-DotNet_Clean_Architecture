namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

internal sealed class TodoItemRecord
{
    public Guid Id { get; set; }

    public Guid TodoListId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<TodoItemTagRecord> Tags { get; } = [];
}
