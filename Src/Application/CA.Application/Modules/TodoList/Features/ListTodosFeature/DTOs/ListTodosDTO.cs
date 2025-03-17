using CA.Domain.Modules.TodoList.Entity;
using System.Text.Json.Serialization;

namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;

public class ListTodosDTO
{
#pragma warning disable
    public ListTodosDTO()
    {
    }
#pragma warning restore

    public ListTodosDTO(ListTodos listTodos)
    {
        Id = listTodos.Id;
        Name = listTodos.Name;
        UserId = listTodos.UserId;
    }

    public Guid Id { get; set; }
    public string Name { get; set; }

    [JsonIgnore]
    public Guid UserId { get; set; }
}
