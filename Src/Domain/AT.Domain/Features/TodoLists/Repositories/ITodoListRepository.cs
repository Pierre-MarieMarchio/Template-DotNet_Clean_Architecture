using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;

namespace AppTemplate.Domain.Features.TodoLists.Repositories;

public interface ITodoListRepository
{
    Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TodoList>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken);

    void Add(TodoList todoList);

    void Remove(TodoList todoList);
}
