namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

/// <summary>
/// One tag on one item. Its primary key is <c>(TodoItemId, Value)</c>, which makes a duplicate tag on
/// an item unrepresentable in the database as well as in the domain.
/// <para>
/// Mapped as an ordinary entity rather than as an owned collection. Owned types are loaded and
/// reconciled implicitly, which is convenient right up to the point where reconciliation is something
/// this layer performs by hand — and then implicit behaviour is exactly what makes it hard to say
/// which rows were written. An explicit entity with an explicit <c>Include</c> costs one line and
/// keeps the answer visible.
/// </para>
/// </summary>
internal sealed class TodoItemTagRecord
{
    public Guid TodoItemId { get; set; }

    public string Value { get; set; } = string.Empty;
}
