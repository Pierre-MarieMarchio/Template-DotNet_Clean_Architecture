using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;

internal interface ITodoListMapper
{
    TodoList ToAggregate(TodoListRecord record);

    TodoListRecord ToNewRecord(TodoList aggregate);

    bool WriteTo(TodoList aggregate, TodoListRecord record);
}
