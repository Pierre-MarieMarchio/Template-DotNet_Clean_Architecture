using CA.Domain.Commons.Bases;

namespace CA.Domain.Modules.TodoList.Entity;

public class TodoItem : AuditableBaseEntity
{
    public string? Title { get; set; }
    public string? Description { get; set; }

    public Guid TodoListId { get; set; }
    public ListTodos? TodoList { get; set; }

    public ICollection<TodoTag>? TodoTags { get; set; }

    public Guid UserId { get; set; }
}
