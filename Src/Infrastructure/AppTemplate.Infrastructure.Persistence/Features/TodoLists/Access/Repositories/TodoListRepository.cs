using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Repositories;

/// <summary>Loads and stages whole <see cref="TodoList"/> aggregates. Nothing here calls
/// <c>SaveChangesAsync</c>: it borrows the context and never owns the transaction — committing belongs
/// to <c>IUnitOfWork</c>. A load is a query for rows, a mapping and a registration in the identity
/// map; a stage is a mapping plus an <c>Add</c> — the price of EF not mapping the domain, which the
/// alternative (handing EF the aggregate) would have cost the domain its independence from the
/// schema. Internal and sealed: an adapter for a port the application layer declares; use cases
/// depend on <see cref="ITodoListRepository"/>.</summary>
internal sealed class TodoListRepository(
    AppDbContext context,
    ITodoListMapper mapper,
    ITodoListTracker tracker) : ITodoListRepository
{
    public async Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // The identity map first: two use cases in one request must get the same object, or the flush
        // would keep whichever copy it saw last.
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        var record = await context.TodoLists
            // The children are a correctness requirement, not an optimisation: unique titles and the
            // item cap can only be checked against all of them, and tags are part of an item's state.
            .Include(list => list.Items)
            .ThenInclude(item => item.Tags)

            // One query per collection: a single join returns root x items x tags, so 100 items with
            // 5 tags each would arrive as 500 copies of the root row.
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

        // The same eager load as GetAsync, for the same correctness reason, over every list the
        // owner holds rather than one.
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

        // Tracked like any other, so a mutation made after Add — and the events Create raised — are
        // mapped onto this row before the save.
        tracker.Track(todoList, record);
    }

    public void Remove(TodoList todoList)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        // Ordinarily already tracked, because a delete follows a load. The fallback attaches a stub
        // carrying the key and the version, so the delete still happens and is still checked against
        // the token the caller decided on.
        var record = tracker.FindRecord(todoList.Id)
            ?? new TodoListRecord { Id = todoList.Id, Version = todoList.Version };

        context.TodoLists.Remove(record);
        tracker.MarkRemoved(todoList, record);
    }
}
