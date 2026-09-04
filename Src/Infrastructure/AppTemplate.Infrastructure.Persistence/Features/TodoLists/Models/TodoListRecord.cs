using AppTemplate.Domain.Common.Abstractions;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

/// <summary>
/// How a to-do list is stored. This is not the aggregate: it is a row, with settable properties,
/// no behaviour and no invariants. EF maps rows, never the domain entities: the aggregate is
/// answerable only to its own rules, the row only to the schema, and the mapper together with the
/// flush pipeline pays for the separation in one place.
/// <para>
/// It implements <see cref="IAuditable"/> so the shared audit interceptor keeps working untouched:
/// the record is what has audit columns, so the record is what gets stamped. The audit values flow
/// record → aggregate on load and after a save, never the other way, which is what makes it
/// impossible for a mapper to overwrite them.
/// </para>
/// </summary>
internal sealed class TodoListRecord : IAuditable
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// PostgreSQL's <c>xmin</c> system column. Never written by this process: the database moves it on
    /// every <c>UPDATE</c>, EF reads it back, and the value goes into the <c>WHERE</c> clause of the
    /// next write.
    /// </summary>
    public uint Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    public Guid? LastModifiedBy { get; set; }

    /// <summary>
    /// A settable collection, deliberately: reconciliation adds to it and removes from it, and EF's
    /// change tracker turns those into <c>INSERT</c>s and <c>DELETE</c>s. The domain's own
    /// <c>Items</c> is read-only over a private list, which is the difference between a model that
    /// enforces something and a row that stores it.
    /// </summary>
    public ICollection<TodoItemRecord> Items { get; } = new List<TodoItemRecord>();

    void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
    {
        LastModifiedAt = at;
        LastModifiedBy = by;
    }
}
