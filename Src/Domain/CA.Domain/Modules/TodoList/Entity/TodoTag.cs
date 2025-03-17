using CA.Domain.Commons.Bases;

namespace CA.Domain.Modules.TodoList.Entity;

public class TodoTag : AuditableBaseEntity
{
    public string? Name { get; set; }

    public ICollection<TodoItem>? TodoItems { get; set; }

    public Guid UserId { get; set; }
}
