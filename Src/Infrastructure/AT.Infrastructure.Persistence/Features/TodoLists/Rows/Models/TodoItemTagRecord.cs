namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

internal sealed class TodoItemTagRecord
{
    public Guid TodoItemId { get; set; }

    public string Value { get; set; } = string.Empty;
}
