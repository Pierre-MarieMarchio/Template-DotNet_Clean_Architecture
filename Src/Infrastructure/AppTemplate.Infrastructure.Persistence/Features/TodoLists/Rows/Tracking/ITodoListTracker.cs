using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;

internal interface ITodoListTracker : IAggregateFlusher, IDomainEventSource
{
    TodoList? Find(Guid id);

    void Track(TodoList aggregate, TodoListRecord record);

    TodoListRecord? FindRecord(Guid id);

    void MarkRemoved(TodoList aggregate, TodoListRecord record);
}
