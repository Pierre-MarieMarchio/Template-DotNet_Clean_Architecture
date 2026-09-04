using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Events;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Domain.Features.Reminders.Entities;

/// <summary>
/// A notification promised for a to-do item at a given instant. An aggregate of one: it holds no
/// child entities, because nothing about a reminder needs to be consistent with anything else in
/// the same transaction.
/// <para>
/// It names the item it is about rather than living inside <c>TodoList</c>. A reminder is fired by
/// a background host that reads by due date across every owner, and putting it inside the list
/// would make each firing load an aggregate — with its items and their tags — to reach one row.
/// </para>
/// </summary>
public sealed class Reminder : AggregateRoot<Guid>, IAuditable, IVersioned
{
    private Reminder(Guid id, Guid ownerId, Guid todoListId, Guid todoItemId, DateTimeOffset dueAt)
        : base(id)
    {
        OwnerId = ownerId;
        TodoListId = todoListId;
        TodoItemId = todoItemId;
        DueAt = dueAt;
    }

    /// <summary>
    /// Copied from the list rather than reached through it, so firing can authorise and address a
    /// reminder without loading an aggregate from another feature. Safe because ownership is
    /// assigned once and never changes.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Carried so that deleting a list can retire its reminders without first reading the items it
    /// is about to remove.
    /// <para>
    /// This is a copy, and it is only correct while an item cannot move between lists — which is
    /// the case here, <c>TodoItem.TodoListId</c> having no mutator. A feature that moves items
    /// would have to update this alongside, or it goes quietly stale.
    /// </para>
    /// </summary>
    public Guid TodoListId { get; private set; }

    public Guid TodoItemId { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public ReminderState State { get; private set; } = ReminderState.Pending;

    /// <summary>
    /// When a host took responsibility for firing this reminder, cleared again if that attempt is
    /// abandoned. Separate from <see cref="NotifiedAt"/> on purpose: with a single flag the state
    /// "claimed, never notified" would not exist in the store, and an attempt lost to a crash
    /// would be indistinguishable from one that never started.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    /// <summary>When the notification actually went out.</summary>
    public DateTimeOffset? NotifiedAt { get; private set; }

    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public Guid? LastModifiedBy { get; private set; }

    /// <param name="now">Injected rather than read from the clock, so the aggregate has no ambient
    /// dependency and its behaviour is reproducible in a test.</param>
    public static Reminder Schedule(
        Guid ownerId,
        Guid todoListId,
        Guid todoItemId,
        DateTimeOffset dueAt,
        DateTimeOffset now)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainException("A reminder must have an owner.");
        }

        if (todoListId == Guid.Empty || todoItemId == Guid.Empty)
        {
            throw new DomainException("A reminder must name the to-do item it is about.");
        }

        if (dueAt == default)
        {
            throw new DomainException("A reminder must have a due date.");
        }

        if (dueAt <= now)
        {
            throw new DomainException("A reminder must be due in the future.");
        }

        return new Reminder(Guid.CreateVersion7(), ownerId, todoListId, todoItemId, dueAt);
    }

    /// <summary>
    /// Rebuilds a reminder that already exists in a store, from the values that were stored. A row
    /// whose state and instants contradict each other is refused on the way in, rather than
    /// becoming an aggregate that cannot honour its own rules.
    /// <para>
    /// One rule is deliberately <em>not</em> re-checked: that <see cref="DueAt"/> is in the future.
    /// That is a precondition of scheduling, not a property of the state — it stops being true by
    /// the mere passing of time, and enforcing it here would refuse to load exactly the rows the
    /// firing query exists to find.
    /// </para>
    /// </summary>
    public static Reminder Rehydrate(
        Guid id,
        Guid ownerId,
        Guid todoListId,
        Guid todoItemId,
        DateTimeOffset dueAt,
        ReminderState state,
        DateTimeOffset? claimedAt,
        DateTimeOffset? notifiedAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("A stored reminder must have an id.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new DomainException("A stored reminder must have an owner.");
        }

        if (todoListId == Guid.Empty || todoItemId == Guid.Empty)
        {
            throw new DomainException("A stored reminder must name the to-do item it is about.");
        }

        // A fired reminder keeps the claim it was notified under — MarkNotified requires one and
        // does not clear it. Cancel does clear it, so a cancelled row still holding one is a row no
        // sequence of operations could have written.
        if (state == ReminderState.Cancelled && claimedAt is not null)
        {
            throw new DomainException("A cancelled reminder cannot still hold a claim.");
        }

        if (state == ReminderState.Fired && notifiedAt is null)
        {
            throw new DomainException("A fired reminder must record when it was notified.");
        }

        if (state != ReminderState.Fired && notifiedAt is not null)
        {
            throw new DomainException("Only a fired reminder can record a notification instant.");
        }

        return new Reminder(id, ownerId, todoListId, todoItemId, dueAt)
        {
            State = state,
            ClaimedAt = claimedAt,
            NotifiedAt = notifiedAt,
        };
    }

    /// <summary>
    /// Takes responsibility for firing this reminder. A claim that has not produced a notification
    /// within <paramref name="staleAfter"/> is taken over: a host that died mid-attempt must not
    /// hold a reminder for ever.
    /// </summary>
    /// <returns><c>true</c> when the claim was taken, <c>false</c> when another host holds a fresh
    /// one — in which case this host must leave the reminder alone.</returns>
    public bool TryClaim(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (State != ReminderState.Pending || DueAt > now)
        {
            return false;
        }

        if (ClaimedAt is { } claimedAt && now - claimedAt < staleAfter)
        {
            return false;
        }

        ClaimedAt = now;

        return true;
    }

    /// <summary>
    /// Records that the notification went out, which is the only thing that retires a reminder for
    /// good.
    /// </summary>
    public void MarkNotified(DateTimeOffset notifiedAt)
    {
        if (State != ReminderState.Pending)
        {
            throw new DomainException("Only a pending reminder can be marked as notified.");
        }

        if (ClaimedAt is null)
        {
            throw new DomainException("A reminder must be claimed before it is notified.");
        }

        State = ReminderState.Fired;
        NotifiedAt = notifiedAt;

        RaiseDomainEvent(new ReminderFiredDomainEvent(Id, OwnerId, TodoItemId, notifiedAt));
    }

    /// <summary>
    /// Gives up a claim without firing, so the next pass can pick the reminder up immediately
    /// rather than waiting out the staleness window.
    /// </summary>
    public void ReleaseClaim()
    {
        if (State == ReminderState.Pending)
        {
            ClaimedAt = null;
        }
    }

    /// <summary>
    /// Calls the reminder off. Idempotent by shape — it assigns a state rather than moving one —
    /// so a redelivered cancellation is a no-op instead of an error, and cancelling something
    /// already fired is refused rather than silently rewriting history.
    /// </summary>
    public void Cancel()
    {
        if (State == ReminderState.Fired)
        {
            throw new DomainException("A reminder that has already fired cannot be cancelled.");
        }

        State = ReminderState.Cancelled;
        ClaimedAt = null;
    }

    /// <summary>Moves a pending reminder to a new instant, under the same rule as scheduling.</summary>
    public void Reschedule(DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (State != ReminderState.Pending)
        {
            throw new DomainException("Only a pending reminder can be rescheduled.");
        }

        if (dueAt <= now)
        {
            throw new DomainException("A reminder must be due in the future.");
        }

        DueAt = dueAt;
        ClaimedAt = null;
    }

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

    void IVersioned.SetVersion(uint version) => Version = version;
}
