using CA.Domain.Commons.Bases;

namespace CA.Domain.Modules.TodoList.Entity;

public class ListTodos : AuditableBaseEntity
{
    public required string Name { get; set; }

    public ICollection<TodoItem>? TodoItems { get; set; }

    public required Guid UserId { get; set; }

}
