using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Domain.Modules.TodoList.Entity;
using System.Text.Json.Serialization;

namespace CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;

public class TodoTagDTO
{
#pragma warning disable
    public TodoTagDTO()
    {
    }
#pragma warning restore 

    public TodoTagDTO(TodoTag todoTag)
    {
        Id = todoTag.Id;
        Name = todoTag.Name!;
        TodoItems = (todoTag.TodoItems ?? [])
            .Select(todoItem => new TodoItemDTO { Id = todoItem.Id })
            .ToList();
        UserId = todoTag.UserId;
    }

    public Guid Id { get; set; }
    public string Name { get; set; }

    [JsonIgnore]
    public ICollection<TodoItemDTO>? TodoItems { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
}
