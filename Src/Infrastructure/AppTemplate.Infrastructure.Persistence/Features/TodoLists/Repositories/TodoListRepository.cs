using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Stores;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mappers;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Repositories;

/// <summary>
/// Loads and stages whole <see cref="TodoList"/> aggregates. Nothing here calls
/// <c>SaveChangesAsync</c>: it borrows the context and never owns the transaction. Committing belongs
/// to <c>IUnitOfWork</c>.
/// <para>
/// It is three lines longer than it was, and every one of them is the price of EF not mapping the
/// domain. A load is now a query for rows plus a mapping plus a registration in the identity map; a
/// stage is a mapping plus an <c>Add</c>. The alternative — handing EF the aggregate — is shorter, and
/// costs the domain model its independence from the schema.
/// </para>
/// <para>
/// Internal and sealed: it is an adapter for a port the application layer declares, and nothing outside
/// this assembly has any business naming the type. Use cases depend on
/// <see cref="ITodoListRepository"/>.
/// </para>
/// </summary>
internal sealed class TodoListRepository(
    AppDbContext context,
    ITodoListMapper mapper,
    ITodoListTracker tracker) : ITodoListRepository
{
    public async Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // The identity map first. Two use cases in one request asking for the same list must get the
        // same object, or each would decide against its own copy and the flush would keep whichever it
        // saw last.
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        var record = await context.TodoLists
            // Tracked, deliberately: the tracked row is what the flush pipeline writes onto and what
            // holds the original concurrency token. Loading the children is not an optimisation but a
            // correctness requirement — the aggregate's invariants (unique titles, item cap) can only
            // be checked against all of its items, and tags are part of an item's state.
            .Include(list => list.Items)
            .ThenInclude(item => item.Tags)

            // One query per collection instead of one join. A single join returns root x items x tags,
            // so a list of 100 items carrying 5 tags each arrives as 500 copies of the root row.
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

    public void Add(TodoList todoList)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        var record = mapper.ToNewRecord(todoList);

        context.TodoLists.Add(record);

        // Tracked like any other: the flush pipeline will map onto this row again before the save, which
        // is how a mutation made after Add — and the domain events raised by Create — still land.
        tracker.Track(todoList, record);
    }

    public void Remove(TodoList todoList)
    {
        ArgumentNullException.ThrowIfNull(todoList);

        // Ordinarily the row is already tracked, because a delete follows a load. The fallback attaches
        // a stub carrying the key and the version, so a caller who reconstructed an aggregate elsewhere
        // still gets a delete rather than a silent no-op — and still gets it checked against the token
        // it decided on, because attaching snapshots the current values as the original ones.
        var record = tracker.FindRecord(todoList.Id)
            ?? new TodoListRecord { Id = todoList.Id, Version = todoList.Version };

        context.TodoLists.Remove(record);
        tracker.MarkRemoved(todoList, record);
    }
}
