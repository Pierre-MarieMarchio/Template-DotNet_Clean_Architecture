using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>
/// This is the use case the whole feature's correctness rides on: it is the one place that must
/// stay right independently of whether <c>CancelRemindersOnTodoItemCompletedConsumer</c> ever
/// ran.
/// </summary>
public sealed class FireDueRemindersUseCaseTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;
    private static readonly ReminderRecipient _recipient = new("someone", "someone@example.com");

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
    private readonly ITodoItemQueries _todoItems = Substitute.For<ITodoItemQueries>();
    private readonly IReminderNotifier _notifier = Substitute.For<IReminderNotifier>();
    private readonly IReminderRecipientDirectory _recipients = Substitute.For<IReminderRecipientDirectory>();
    private readonly IReminderDiagnostics _diagnostics = Substitute.For<IReminderDiagnostics>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILeaderLease _lease = Substitute.For<ILeaderLease>();

    public FireDueRemindersUseCaseTests()
    {
        GivenTheLeaseIsGranted();
        _recipients.FindAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(_recipient);
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoDueReminders_ReturnsZeroAndTouchesNothingElse()
    {
        _repository.GetDueAsync(_now, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[]);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _todoItems.DidNotReceive().GetCompletionStatesAsync(
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ReminderNotification>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APendingReminderOnAnIncompleteTarget_IsNotifiedAndMarkedFired()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, false));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        reminder.State.ShouldBe(ReminderState.Fired);
        reminder.NotifiedAt.ShouldBe(_now);
        // A plain value rather than Arg.Is: ReminderNotification is a record, so NSubstitute
        // matches this call by structural equality without needing a predicate at all.
        await _notifier.Received(1).NotifyAsync(
            new ReminderNotification(reminder.OwnerId, _recipient, reminder.TodoItemId, reminder.DueAt),
            TestToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    /// <summary>
    /// An owner who no longer names an account has nobody to ring, and retrying would not change
    /// that: the reminder is retired, not counted as notified.
    /// </summary>
    [Fact]
    public async Task AReminderWhoseOwnerIsGone_IsCancelledInsteadOfNotified()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, false));
        _recipients.FindAsync(reminder.OwnerId, Arg.Any<CancellationToken>()).Returns((ReminderRecipient?)null);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        reminder.State.ShouldBe(ReminderState.Cancelled);
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ReminderNotification>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
        _diagnostics.DidNotReceive().RecordMissedCancellation();
    }

    [Fact]
    public async Task ACompletedTargetStillPending_IsCancelledInsteadOfNotified()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, true));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        reminder.State.ShouldBe(ReminderState.Cancelled);
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ReminderNotification>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// This is the count that exactly equals the completion events
    /// <c>CancelRemindersOnTodoItemCompletedConsumer</c> never received.
    /// </summary>
    [Fact]
    public async Task ACompletedTargetStillPending_RecordsAMissedCancellation()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, true));

        await UseCase().ExecuteAsync(TestToken);

        _diagnostics.Received(1).RecordMissedCancellation();
    }

    /// <summary>
    /// Deleting an item or its list raises no domain event, so there is nothing this could have
    /// missed — unlike <see cref="ACompletedTargetStillPending_RecordsAMissedCancellation"/>, this
    /// is the mechanism working as intended, not a divergence.
    /// </summary>
    [Fact]
    public async Task ATargetAbsentFromTheProjection_IsCancelledWithoutRecordingADivergence()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        // No entry at all for reminder.TodoItemId: the item was deleted, not completed.
        GivenCompletionStates();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        reminder.State.ShouldBe(ReminderState.Cancelled);
        _diagnostics.DidNotReceive().RecordMissedCancellation();
    }

    [Fact]
    public async Task ANotifierFailure_IsRecordedAndDoesNotFailTheRestOfTheBatch()
    {
        var failing = ADueReminder();
        var succeeding = ADueReminder();
        GivenDueReminders(failing, succeeding);
        GivenCompletionStates((failing.TodoItemId, false), (succeeding.TodoItemId, false));
        _notifier.NotifyAsync(
                new ReminderNotification(failing.OwnerId, _recipient, failing.TodoItemId, failing.DueAt),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the relay is down"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        failing.State.ShouldBe(ReminderState.Pending);
        failing.FailedAttempts.ShouldBe(1);
        failing.NextAttemptAt.ShouldBe(_now + Reminder.RetryDelayAfter(1));
        succeeding.State.ShouldBe(ReminderState.Fired);
    }

    /// <summary>
    /// One commit per reminder is what bounds a failed commit to the reminder it belongs to: the
    /// failure of a notification is saved too, so the retry wait it starts is not lost.
    /// </summary>
    [Fact]
    public async Task EveryReminderOfTheBatch_IsCommittedOnItsOwn()
    {
        var reminders = Enumerable.Range(0, 5).Select(_ => ADueReminder()).ToArray();
        GivenDueReminders(reminders);
        GivenCompletionStates([.. reminders.Select(reminder => (reminder.TodoItemId, false))]);

        await UseCase().ExecuteAsync(TestToken);

        await _unitOfWork.Received(5).SaveChangesAsync(TestToken);
    }

    [Fact]
    public async Task ACancelledReminder_IsCommittedOnItsOwnToo()
    {
        var completed = ADueReminder();
        var outstanding = ADueReminder();
        GivenDueReminders(completed, outstanding);
        GivenCompletionStates((completed.TodoItemId, true), (outstanding.TodoItemId, false));

        await UseCase().ExecuteAsync(TestToken);

        await _unitOfWork.Received(2).SaveChangesAsync(TestToken);
    }

    /// <summary>
    /// The missed-cancellation count is the number of completion events that never arrived, so it
    /// must not be incremented for a cancellation that did not commit and will be found again.
    /// </summary>
    [Fact]
    public async Task ACancellationThatLosesItsRace_IsNotCountedAsAMissedEvent()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, true));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("moved"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        _diagnostics.DidNotReceive().RecordMissedCancellation();
    }

    /// <summary>
    /// A failed write stays staged in the scope, so carrying on would only meet the same conflict
    /// again at every later save. The pass ends instead, and the rest wait for the next one.
    /// </summary>
    [Fact]
    public async Task AConflict_EndsThePassWithoutTouchingTheReminderAfterIt()
    {
        var raced = ADueReminder();
        var after = ADueReminder();
        GivenDueReminders(raced, after);
        GivenCompletionStates((raced.TodoItemId, false), (after.TodoItemId, false));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("moved"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0, "a notification whose commit lost is not counted as delivered");
        await _notifier.DidNotReceive().NotifyAsync(
            new ReminderNotification(after.OwnerId, _recipient, after.TodoItemId, after.DueAt),
            Arg.Any<CancellationToken>());
        after.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public async Task TheCompletionCheck_IsOneCallForTheWholeBatch()
    {
        var a = ADueReminder();
        var b = ADueReminder();
        GivenDueReminders(a, b);
        GivenCompletionStates((a.TodoItemId, false), (b.TodoItemId, false));

        await UseCase().ExecuteAsync(TestToken);

        await _todoItems.Received(1).GetCompletionStatesAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids!.Count == 2 && ids.Contains(a.TodoItemId) && ids.Contains(b.TodoItemId)),
            TestToken);
    }

    /// <summary>
    /// A pass that is not the leader is not a failure and not an error to report: another host is
    /// firing this batch. What it must not do is any of the work.
    /// </summary>
    [Fact]
    public async Task TheLeaseHeldByAnotherHost_LeavesTheWholePassUndone()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, false));
        GivenTheLeaseIsHeldElsewhere();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(0);
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<ReminderNotification>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().GetDueAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    /// <summary>
    /// The lease name is the lock key every replica derives, including the two releases running
    /// side by side during a rolling deploy. The literal is repeated here on purpose: that is what
    /// makes renaming the constant fail a test instead of silently splitting the leader in two.
    /// </summary>
    [Fact]
    public async Task TheWholePass_RunsUnderOneNamedLease()
    {
        var reminder = ADueReminder();
        GivenDueReminders(reminder);
        GivenCompletionStates((reminder.TodoItemId, false));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        await _lease.Received(1).TryRunExclusivelyAsync(
            "apptemplate.reminders.fire-due",
            Arg.Any<Func<CancellationToken, Task>>(),
            TestToken);
    }

    /// <summary>
    /// A failure inside the work must not come back looking like a standby pass: the two answers
    /// mean opposite things to whoever reads the log.
    /// </summary>
    [Fact]
    public async Task AFailureInsideTheLease_SurfacesToTheCaller()
    {
        _repository.GetDueAsync(_now, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the due query is broken"));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await UseCase().ExecuteAsync(TestToken));

        exception.Message.ShouldBe("the due query is broken");
    }

    private FireDueRemindersUseCase UseCase() => new(
        _repository,
        _todoItems,
        _notifier,
        _recipients,
        _diagnostics,
        _unitOfWork,
        new StubDateTimeProvider(_now),
        _lease,
        NullLogger<FireDueRemindersUseCase>.Instance);

    /// <summary>
    /// The substitute has to <em>call</em> the delegate, or every test here would assert against a
    /// pass that never ran. The lambda is async and returns <c>Task&lt;bool&gt;</c> so that the work
    /// is awaited inside the call rather than abandoned: a non-async one would return true while
    /// the work was still an unobserved task, and an exception it threw would surface nowhere.
    /// </summary>
    private void GivenTheLeaseIsGranted() =>
        _lease.TryRunExclusivelyAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await call.Arg<Func<CancellationToken, Task>>()!(call.Arg<CancellationToken>());

                return true;
            });

    private void GivenTheLeaseIsHeldElsewhere() =>
        _lease.TryRunExclusivelyAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

    private static Reminder ADueReminder() =>
        AReminder.Rehydrated(_ownerId, dueAt: _now.AddMinutes(-1));

    private void GivenDueReminders(params Reminder[] reminders) =>
        _repository.GetDueAsync(_now, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)reminders);

    private void GivenCompletionStates(params (Guid TodoItemId, bool IsCompleted)[] states)
    {
        var dictionary = states.ToDictionary(state => state.TodoItemId, state => state.IsCompleted);
        _todoItems.GetCompletionStatesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<Guid, bool>)dictionary);
    }
}
