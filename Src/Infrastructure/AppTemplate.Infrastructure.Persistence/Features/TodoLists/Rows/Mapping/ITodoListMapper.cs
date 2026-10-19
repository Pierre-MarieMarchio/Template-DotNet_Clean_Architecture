using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;

/// <summary>Translates between the <see cref="TodoList"/> aggregate and the rows that store it.
/// <b>The single most dangerous object in the persistence layer:</b> a mapper that forgets a property
/// silently loses data — the reflection-driven round-trip test is what fails when a property does not
/// survive aggregate → record → aggregate. Three operations, and the asymmetry is the design:
/// <see cref="ToAggregate"/> reads everything the store holds into domain state, concurrency token and
/// audit values included; <see cref="ToNewRecord"/> inserts a fresh row; <see cref="WriteTo"/> assigns
/// onto a <em>tracked</em> row and lets EF work out what changed, never replacing the row object and
/// never touching the audit columns with anything other than the values it was loaded with.</summary>
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

    /// <summary>Writes the aggregate's current state onto an already-tracked row, reconciling the
    /// item and tag collections: children present in both are assigned onto, children only in the
    /// aggregate are added, children only in the row are removed.</summary>
    /// <returns><c>true</c> when a child row was added or removed — the caller's cue that the root
    /// must be marked modified, so its concurrency token moves even when none of its own columns
    /// did.</returns>
    bool WriteTo(TodoList aggregate, TodoListRecord record);
}
