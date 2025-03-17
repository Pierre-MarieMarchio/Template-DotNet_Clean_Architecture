using CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;
using CA.Domain.Modules.TodoList.Entity;
using System.Text.Json.Serialization;

namespace CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;

public class TodoItemDTO
{
#pragma warning disable
    public TodoItemDTO()
    {
    }
#pragma warning restore 

    public TodoItemDTO(TodoItem todoItem)
    {
        Id = todoItem.Id;
        Title = todoItem.Title;
        Description = todoItem.Description;
        TodoTags = (todoItem.TodoTags ?? [])
            .Select(tag => new TodoTagDTO { Id = tag.Id })
            .ToList();
        TodoListId = todoItem.TodoListId;
        UserId = todoItem.UserId;
    }

    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public ICollection<TodoTagDTO> TodoTags { get; set; }
    public Guid TodoListId { get; set; }

    [JsonIgnore]
    public Guid UserId { get; set; }
}