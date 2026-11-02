using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Repositories;

internal sealed class TodoListRepository(
    AppDbContext context,
    ITodoListMapper mapper,
    ITodoListTracker tracker) : ITodoListRepository
{
    public async Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        var record = await context.TodoLists
            .Include(list => list.Items)
            .ThenInclude(item => item.Tags)

            .AsSplitQuery()
            .FirstOrDefaultAsync(list => list.Id == id, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var aggregate = mapper.ToAggregate(record);
        tracker.Track(aggregate, record);

        return aggregate;
    }

    public async Task<IReadOnlyList<TodoList>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var records = await context.TodoLists
            .Include(list => list.Items)
            .ThenInclude(item => item.Tags)
            .AsSplitQuery()
            .Where(list => list.OwnerId == ownerId.Value)
            .ToListAsync(cancellationToken);

        var aggregates = new List<TodoList>(records.Count);

        foreach (var record in records)
        {
            var aggregate = mapper.ToAggregate(record);

            tracker.Track(aggregate, record);
            aggregates.Add(aggregate);
        }

        return aggregates;
    }

    public void Add(TodoList todoList)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        var record = mapper.ToNewRecord(todoList);

        context.TodoLists.Add(record);

        tracker.Track(todoList, record);
    }

    public void Remove(TodoList todoList)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        var record = tracker.FindRecord(todoList.Id)
            ?? new TodoListRecord { Id = todoList.Id, Version = todoList.Version };

        context.TodoLists.Remove(record);
        tracker.MarkRemoved(todoList, record);
    }
}
