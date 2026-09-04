using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;

/// <summary>
/// How a reminder is stored: a row, with settable properties, no behaviour and no invariants. The
/// aggregate that owns the rules is <see cref="Domain.Features.Reminders.Entities.Reminder"/>; this type
/// answers only to the schema.
/// <para>
/// <see cref="ReminderState"/> is reused as-is rather than given a persistence-side twin: it is a plain
/// enumeration with no method and no rule attached to it, so it carries none of the constraints — a
/// complex property, an owned collection, a derived value — that would otherwise make the storage shape
/// dictate how the domain expresses itself.
/// </para>
/// </summary>
internal sealed class ReminderRecord : IAuditable
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid TodoListId { get; set; }

    public Guid TodoItemId { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public ReminderState State { get; set; } = ReminderState.Pending;

    public DateTimeOffset? ClaimedAt { get; set; }

    public DateTimeOffset? NotifiedAt { get; set; }

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
