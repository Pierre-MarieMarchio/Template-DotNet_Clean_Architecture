using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mappers;

/// <summary>
/// Translates between the <see cref="TodoList"/> aggregate and the rows that store it.
/// <para>
/// <b>This type is the single most dangerous object in the persistence layer,</b> and the interface
/// exists partly to say so. A mapper that forgets a property throws nothing, logs nothing and fails
/// no test that was not written for it — it silently loses data, and the loss surfaces later as a
/// value that "reset itself". A reflection-driven round-trip test enumerates the aggregate's state
/// and fails when a property does not survive aggregate → record → aggregate, so the guarantee does
/// not rest on a reviewer noticing.
/// </para>
/// <para>
/// Three operations, and the asymmetry between them is the design:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ToAggregate"/> — read: everything the store holds becomes domain
/// state, including the concurrency token and the audit values.</description></item>
/// <item><description><see cref="ToNewRecord"/> — insert: a fresh row for an aggregate the store has
/// never seen.</description></item>
/// <item><description><see cref="WriteTo"/> — update: assign onto a <em>tracked</em> row and let EF
/// work out what changed. It never replaces the row object, and it never touches the audit columns
/// with anything other than the values it was loaded with.</description></item>
/// </list>
/// </summary>
internal interface ITodoListMapper
{
    /// <summary>
    /// Rebuilds an aggregate from a row and its children. The row must have been loaded with its
    /// items and their tags; a partially loaded graph would produce an aggregate that quietly claims
    /// to have no items, and its invariants would then be checked against nothing.
    /// </summary>
    TodoList ToAggregate(TodoListRecord record);

    /// <summary>Builds the row graph for an aggregate that has never been stored.</summary>
    TodoListRecord ToNewRecord(TodoList aggregate);

    /// <summary>
    /// Writes the aggregate's current state onto an already-tracked row, reconciling the item and tag
    /// collections: children present in both are assigned onto (so EF writes only the ones that
    /// really changed), children only in the aggregate are added, children only in the row are
    /// removed.
    /// </summary>
    /// <returns><c>true</c> when a child row was added or removed. The caller needs this to decide
    /// whether the root has to be marked modified — a change to a child is a change to its aggregate,
    /// so the root's concurrency token must move even when none of its own columns did.</returns>
    bool WriteTo(TodoList aggregate, TodoListRecord record);
}
