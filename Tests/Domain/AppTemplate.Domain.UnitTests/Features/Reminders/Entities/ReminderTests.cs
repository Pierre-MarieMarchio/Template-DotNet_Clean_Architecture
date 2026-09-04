using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Events;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Reminders.Entities;

public sealed class ReminderTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid _ownerId = Guid.CreateVersion7();
    private static readonly Guid _todoListId = Guid.CreateVersion7();
    private static readonly Guid _todoItemId = Guid.CreateVersion7();

    private static Reminder AReminder(DateTimeOffset? dueAt = null, DateTimeOffset? scheduledAt = null) =>
        Reminder.Schedule(_ownerId, _todoListId, _todoItemId, dueAt ?? _now.AddHours(1), scheduledAt ?? _now);

    /// <summary>
    /// Builds a reminder through the load path rather than <see cref="Reminder.Schedule"/>, which is
    /// the only way to put a reminder in a state <c>Schedule</c> could never produce itself — overdue,
    /// pre-claimed, or already retired.
    /// </summary>
    private static Reminder ARehydratedReminder(
        Guid? id = null,
        ReminderState state = ReminderState.Pending,
        DateTimeOffset? dueAt = null,
        DateTimeOffset? claimedAt = null,
        DateTimeOffset? notifiedAt = null) =>
        Reminder.Rehydrate(
            id ?? Guid.CreateVersion7(),
            _ownerId,
            _todoListId,
            _todoItemId,
            dueAt ?? _now.AddDays(-1),
            state,
            claimedAt,
            notifiedAt);

    #region Scheduling

    [Fact]
    public void Schedule_Rejects_AnEmptyOwnerId()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(Guid.Empty, _todoListId, _todoItemId, _now.AddHours(1), _now));

        exception.Message.ShouldContain("owner");
    }

    [Fact]
    public void Schedule_Rejects_AnEmptyTodoListId()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, Guid.Empty, _todoItemId, _now.AddHours(1), _now));

        exception.Message.ShouldContain("to-do item");
    }

    [Fact]
    public void Schedule_Rejects_AnEmptyTodoItemId()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, _todoListId, Guid.Empty, _now.AddHours(1), _now));

        exception.Message.ShouldContain("to-do item");
    }

    /// <summary>
    /// The default instant is what an uninitialised caller passes. Accepting it would schedule a
    /// reminder for 0001-01-01, a value indistinguishable from "never set".
    /// </summary>
    [Fact]
    public void Schedule_Rejects_TheDefaultDueDate()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, _todoListId, _todoItemId, default, _now));

        exception.Message.ShouldContain("due date");
    }

    [Fact]
    public void Schedule_Rejects_ADueDateInThePast() =>
        Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, _todoListId, _todoItemId, _now.AddMinutes(-1), _now));

    /// <summary>
    /// A reminder due at the very instant it is scheduled would never be reachable by a firing pass
    /// that reads strictly-due rows: it must be pushed at least an instant into the future.
    /// </summary>
    [Fact]
    public void Schedule_Rejects_ADueDateEqualToNow() =>
        Should.Throw<DomainException>(() => Reminder.Schedule(_ownerId, _todoListId, _todoItemId, _now, _now));

    [Fact]
    public void Schedule_Accepts_ADueDateInTheFuture()
    {
        var dueAt = _now.AddHours(1);

        var reminder = Reminder.Schedule(_ownerId, _todoListId, _todoItemId, dueAt, _now);

        reminder.Id.ShouldNotBe(Guid.Empty);
        reminder.OwnerId.ShouldBe(_ownerId);
        reminder.TodoListId.ShouldBe(_todoListId);
        reminder.TodoItemId.ShouldBe(_todoItemId);
        reminder.DueAt.ShouldBe(dueAt);
        reminder.State.ShouldBe(ReminderState.Pending);
        reminder.ClaimedAt.ShouldBeNull();
        reminder.NotifiedAt.ShouldBeNull();
    }

    [Fact]
    public void Schedule_GivesEveryReminderADistinctId() => AReminder().Id.ShouldNotBe(AReminder().Id);

    #endregion

    #region Rehydrating

    [Fact]
    public void Rehydrate_Rejects_AnEmptyId() =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(Guid.Empty, _ownerId, _todoListId, _todoItemId, _now.AddHours(1), ReminderState.Pending, null, null));

    /// <summary>
    /// The single most important test in this file. Being in the future is a precondition of
    /// <see cref="Reminder.Schedule"/>, not a property the aggregate holds forever: a reminder stops
    /// satisfying it by the mere passing of time, and every reminder the firing query is meant to find
    /// is, by definition, one for which it no longer holds. A <c>Rehydrate</c> that re-checked it would
    /// throw while loading exactly the rows it exists to load, and the feature could never fire anything.
    /// This test loading an overdue reminder successfully is the proof that firing can work at all.
    /// </summary>
    [Fact]
    public void Rehydrate_DoesNotRecheckThatTheDueDateIsInTheFuture()
    {
        var overdueBy = _now.AddDays(-1);

        var reminder = Should.NotThrow(
            () => Reminder.Rehydrate(Guid.CreateVersion7(), _ownerId, _todoListId, _todoItemId, overdueBy, ReminderState.Pending, null, null));

        reminder.DueAt.ShouldBe(overdueBy);
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public void Rehydrate_RestoresTheStoredIdentityAndValues()
    {
        var id = Guid.CreateVersion7();
        var dueAt = _now.AddHours(2);
        var claimedAt = _now.AddMinutes(-1);

        var reminder = Reminder.Rehydrate(id, _ownerId, _todoListId, _todoItemId, dueAt, ReminderState.Pending, claimedAt, null);

        reminder.Id.ShouldBe(id);
        reminder.OwnerId.ShouldBe(_ownerId);
        reminder.TodoListId.ShouldBe(_todoListId);
        reminder.TodoItemId.ShouldBe(_todoItemId);
        reminder.DueAt.ShouldBe(dueAt);
        reminder.State.ShouldBe(ReminderState.Pending);
        reminder.ClaimedAt.ShouldBe(claimedAt);
        reminder.NotifiedAt.ShouldBeNull();
    }

    [Fact]
    public void Rehydrate_RaisesNoDomainEvent() =>
        ARehydratedReminder().DomainEvents.ShouldBeEmpty();

    #endregion

    #region Claiming

    /// <summary>
    /// Isolated from every other reason <c>TryClaim</c> can refuse: the reminder is otherwise a
    /// perfectly claimable one, just not due yet.
    /// </summary>
    [Fact]
    public void TryClaim_ReturnsFalse_WhenTheReminderIsNotYetDue()
    {
        var reminder = AReminder(dueAt: _now.AddHours(1));

        reminder.TryClaim(_now, TimeSpan.FromMinutes(5)).ShouldBeFalse();
        reminder.ClaimedAt.ShouldBeNull();
    }

    /// <summary>
    /// The reminder is due and unclaimed — the only thing wrong with it is its state — so a claim
    /// that succeeded here would prove the state guard is missing rather than merely redundant with
    /// the due-date guard.
    /// </summary>
    [Fact]
    public void TryClaim_ReturnsFalse_WhenTheReminderIsNotPending()
    {
        var reminder = ARehydratedReminder(state: ReminderState.Cancelled, dueAt: _now.AddDays(-1));

        reminder.TryClaim(_now, TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    /// <summary>
    /// The existing claim is well within the staleness window, so nothing else about the reminder may
    /// account for the refusal.
    /// </summary>
    [Fact]
    public void TryClaim_ReturnsFalse_WhenAnotherHostHoldsAFreshClaim()
    {
        var claimedAt = _now.AddMinutes(-4);
        var reminder = ARehydratedReminder(claimedAt: claimedAt);

        reminder.TryClaim(_now, TimeSpan.FromMinutes(5)).ShouldBeFalse();
        reminder.ClaimedAt.ShouldBe(claimedAt);
    }

    /// <summary>
    /// What lets a host that died mid-attempt be recovered from: without this, one crash would hold a
    /// reminder unclaimable for ever.
    /// </summary>
    [Fact]
    public void TryClaim_ReturnsTrue_WhenTheExistingClaimIsOlderThanTheStaleAfterWindow()
    {
        var claimedAt = _now.AddMinutes(-10);
        var reminder = ARehydratedReminder(claimedAt: claimedAt);

        var claimed = reminder.TryClaim(_now, TimeSpan.FromMinutes(5));

        claimed.ShouldBeTrue();
        reminder.ClaimedAt.ShouldBe(_now);
    }

    /// <summary>
    /// Pins the exact boundary: the XML doc on <see cref="Reminder.TryClaim"/> says a claim "older
    /// than <c>staleAfter</c>" is taken over, but the implementation compares with <c>&lt;</c>, so a
    /// claim exactly <c>staleAfter</c> old — not strictly older — is already treated as stale here.
    /// Recorded as a doc/behaviour mismatch rather than fixed, since fixing either side is outside
    /// what a test is allowed to decide.
    /// </summary>
    [Fact]
    public void TryClaim_TakesOverAClaim_ExactlyAtTheStaleAfterThreshold()
    {
        var staleAfter = TimeSpan.FromMinutes(5);
        var claimedAt = _now - staleAfter;
        var reminder = ARehydratedReminder(claimedAt: claimedAt);

        reminder.TryClaim(_now, staleAfter).ShouldBeTrue();
    }

    [Fact]
    public void TryClaim_Succeeds_WhenThereIsNoExistingClaim()
    {
        var reminder = ARehydratedReminder(claimedAt: null);

        reminder.TryClaim(_now, TimeSpan.FromMinutes(5)).ShouldBeTrue();
        reminder.ClaimedAt.ShouldBe(_now);
    }

    [Fact]
    public void TryClaim_Accepts_TheExactDueInstant()
    {
        var dueAt = _now;
        var reminder = ARehydratedReminder(dueAt: dueAt);

        reminder.TryClaim(dueAt, TimeSpan.FromMinutes(5)).ShouldBeTrue();
    }

    #endregion

    #region Marking as notified

    /// <summary>
    /// A pending, due-or-not reminder that nobody claimed. <c>MarkNotified</c> has no due-date check of
    /// its own, so the claim is the only thing standing between "pending" and "fired" — this proves it
    /// actually stands there.
    /// </summary>
    [Fact]
    public void MarkNotified_Rejects_AReminderThatWasNeverClaimed()
    {
        var reminder = AReminder();

        var exception = Should.Throw<DomainException>(() => reminder.MarkNotified(_now));

        exception.Message.ShouldContain("claimed");
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public void MarkNotified_Rejects_ACancelledReminder()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        reminder.Cancel();

        Should.Throw<DomainException>(() => reminder.MarkNotified(_now));
    }

    [Fact]
    public void MarkNotified_Rejects_AReminderAlreadyFired()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        reminder.MarkNotified(_now);

        Should.Throw<DomainException>(() => reminder.MarkNotified(_now.AddMinutes(1)));
    }

    [Fact]
    public void MarkNotified_Transitions_AClaimedReminderToFired()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        var notifiedAt = _now.AddSeconds(1);

        reminder.MarkNotified(notifiedAt);

        reminder.State.ShouldBe(ReminderState.Fired);
        reminder.NotifiedAt.ShouldBe(notifiedAt);
    }

    #endregion

    #region Cancelling

    [Fact]
    public void Cancel_MovesAPendingReminderToCancelled()
    {
        var reminder = AReminder();

        reminder.Cancel();

        reminder.State.ShouldBe(ReminderState.Cancelled);
    }

    [Fact]
    public void Cancel_Rejects_AReminderAlreadyFired()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        reminder.MarkNotified(_now);

        Should.Throw<DomainException>(() => reminder.Cancel());
    }

    /// <summary>
    /// A property of shape, not of history: <c>Cancel</c> assigns a state rather than moving one, so a
    /// redelivered cancellation lands on the same value instead of being rejected as an illegal
    /// transition from "cancelled" to "cancelled".
    /// </summary>
    [Fact]
    public void Cancel_IsIdempotent()
    {
        var reminder = AReminder();

        reminder.Cancel();
        Should.NotThrow(() => reminder.Cancel());

        reminder.State.ShouldBe(ReminderState.Cancelled);
    }

    [Fact]
    public void Cancel_ClearsAnyExistingClaim()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1), claimedAt: _now.AddMinutes(-1));

        reminder.Cancel();

        reminder.ClaimedAt.ShouldBeNull();
    }

    #endregion

    #region Rescheduling

    [Fact]
    public void Reschedule_MovesAPendingReminderToTheNewDueDate()
    {
        var reminder = AReminder();
        var newDueAt = _now.AddDays(1);

        reminder.Reschedule(newDueAt, _now);

        reminder.DueAt.ShouldBe(newDueAt);
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public void Reschedule_ClearsAnyExistingClaim()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1), claimedAt: _now.AddMinutes(-1));

        reminder.Reschedule(_now.AddDays(1), _now);

        reminder.ClaimedAt.ShouldBeNull();
    }

    [Fact]
    public void Reschedule_Rejects_ADueDateInThePast()
    {
        var reminder = AReminder();

        Should.Throw<DomainException>(() => reminder.Reschedule(_now.AddMinutes(-1), _now));
    }

    [Fact]
    public void Reschedule_Rejects_ADueDateEqualToNow()
    {
        var reminder = AReminder();

        Should.Throw<DomainException>(() => reminder.Reschedule(_now, _now));
    }

    [Fact]
    public void Reschedule_Rejects_ACancelledReminder()
    {
        var reminder = AReminder();
        reminder.Cancel();

        Should.Throw<DomainException>(() => reminder.Reschedule(_now.AddDays(1), _now));
    }

    [Fact]
    public void Reschedule_Rejects_AFiredReminder()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        reminder.MarkNotified(_now);

        Should.Throw<DomainException>(() => reminder.Reschedule(_now.AddDays(1), _now));
    }

    #endregion

    #region Releasing a claim

    /// <summary>
    /// The whole point of releasing rather than waiting: the claim above is still well inside the
    /// staleness window, yet the reminder becomes claimable again the instant it is released.
    /// </summary>
    [Fact]
    public void ReleaseClaim_MakesAPendingReminderClaimableImmediately()
    {
        var staleAfter = TimeSpan.FromMinutes(5);
        var reminder = ARehydratedReminder(claimedAt: _now.AddMinutes(-1));

        reminder.ReleaseClaim();

        reminder.ClaimedAt.ShouldBeNull();
        reminder.TryClaim(_now, staleAfter).ShouldBeTrue();
    }

    [Fact]
    public void ReleaseClaim_IsANoOp_OnAFiredReminder()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        reminder.MarkNotified(_now);
        var claimedAt = reminder.ClaimedAt;

        reminder.ReleaseClaim();

        reminder.ClaimedAt.ShouldBe(claimedAt);
    }

    /// <summary>
    /// A cancelled reminder never holds a claim — <c>Cancel</c> clears it, and the load path refuses
    /// a row where the two disagree — so releasing one is a no-op with nothing to release. Asserted
    /// because a caller cannot know a reminder's state before asking.
    /// </summary>
    [Fact]
    public void ReleaseClaim_IsANoOp_OnACancelledReminder()
    {
        var reminder = ARehydratedReminder(state: ReminderState.Cancelled);

        reminder.ReleaseClaim();

        reminder.State.ShouldBe(ReminderState.Cancelled);
        reminder.ClaimedAt.ShouldBeNull();
    }

    /// <summary>
    /// The load path refuses what the aggregate cannot produce: cancelling clears the claim, so a
    /// cancelled row still holding one is a row no sequence of operations could have written.
    /// </summary>
    [Fact]
    public void Rehydrate_Rejects_AClaimHeldByACancelledReminder() =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(
                Guid.CreateVersion7(),
                _ownerId,
                _todoListId,
                _todoItemId,
                _now.AddDays(-1),
                ReminderState.Cancelled,
                _now.AddMinutes(-1),
                null));

    /// <summary>
    /// The converse, and the reason the rule above names only one state: notifying requires a claim
    /// and does not clear it, so a fired reminder keeps the one it fired under.
    /// </summary>
    [Fact]
    public void Rehydrate_Accepts_AClaimHeldByAFiredReminder()
    {
        var claimedAt = _now.AddMinutes(-2);

        var reminder = Should.NotThrow(
            () => Reminder.Rehydrate(
                Guid.CreateVersion7(),
                _ownerId,
                _todoListId,
                _todoItemId,
                _now.AddDays(-1),
                ReminderState.Fired,
                claimedAt,
                _now.AddMinutes(-1)));

        reminder.ClaimedAt.ShouldBe(claimedAt);
    }

    #endregion

    #region Domain events

    [Fact]
    public void MarkNotified_RaisesAReminderFiredDomainEvent_WithTheRemindersOwnValues()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        var notifiedAt = _now.AddSeconds(1);

        reminder.MarkNotified(notifiedAt);

        var raised = reminder.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReminderFiredDomainEvent>();
        raised.ReminderId.ShouldBe(reminder.Id);
        raised.OwnerId.ShouldBe(_ownerId);
        raised.TodoItemId.ShouldBe(_todoItemId);
        raised.OccurredOn.ShouldBe(notifiedAt);
    }

    /// <summary>
    /// The rejected retry must not sneak out a second event: it fails before it reaches the point where
    /// one would be raised.
    /// </summary>
    [Fact]
    public void MarkNotified_RaisesNoAdditionalEvent_WhenARepeatCallIsRejected()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.TryClaim(_now, TimeSpan.FromMinutes(5));
        reminder.MarkNotified(_now);
        reminder.ClearDomainEvents();

        Should.Throw<DomainException>(() => reminder.MarkNotified(_now.AddMinutes(1)));

        reminder.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Schedule_RaisesNoDomainEvent() => AReminder().DomainEvents.ShouldBeEmpty();

    #endregion

    #region Stored rows that contradict themselves

    /// <summary>
    /// The state and the instants are two records of the same fact, and a row where they disagree
    /// describes a reminder that never existed. Loading it would produce an aggregate whose own
    /// rules are already broken, and the damage would surface far from the row that caused it.
    /// </summary>
    [Fact]
    public void Rehydrate_Rejects_AFiredReminderWithNoNotificationInstant() =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(
                Guid.CreateVersion7(),
                _ownerId,
                _todoListId,
                _todoItemId,
                _now.AddDays(-1),
                ReminderState.Fired,
                _now.AddDays(-1),
                null));

    [Theory]
    [InlineData(ReminderState.Pending)]
    [InlineData(ReminderState.Cancelled)]
    public void Rehydrate_Rejects_ANotificationInstantOnAReminderThatNeverFired(ReminderState state) =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(
                Guid.CreateVersion7(),
                _ownerId,
                _todoListId,
                _todoItemId,
                _now.AddDays(-1),
                state,
                null,
                _now.AddDays(-1)));

    #endregion
}
