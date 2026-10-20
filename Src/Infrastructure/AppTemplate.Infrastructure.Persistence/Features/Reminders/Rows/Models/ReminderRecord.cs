using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;

internal sealed class ReminderRecord : IAuditable
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid TodoListId { get; set; }

    public Guid TodoItemId { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public ReminderState State { get; set; } = ReminderState.Pending;

    public DateTimeOffset? NotifiedAt { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

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
