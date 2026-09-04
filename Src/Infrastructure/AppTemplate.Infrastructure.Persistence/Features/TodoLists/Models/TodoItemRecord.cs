namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

/// <summary>
/// How one item of a to-do list is stored. A child row of <see cref="TodoListRecord"/>, with its own
/// table because a list can hold hundreds of items and they are read as rows.
/// <para>
/// There is no <c>IsCompleted</c> column. The domain derives it from <c>CompletedAt</c>, so storing it
/// would create a second copy of the same fact — and a state the two can disagree in. Queries express
/// it as <c>CompletedAt != null</c>, in SQL.
/// </para>
/// </summary>
internal sealed class TodoItemRecord
{
    public Guid Id { get; set; }

    public Guid TodoListId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<TodoItemTagRecord> Tags { get; } = new List<TodoItemTagRecord>();
}
