using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>
/// What a pass guarantees about delivery across several passes and several reminders, against a store
/// that commits atomically per save and refuses a write made against a version that has moved.
/// <para>
/// <b>The level of proof is a use-case test with a faithful double, not a database.</b>
/// <see cref="InMemoryReminderStore"/> reproduces the two properties the behaviour turns on — a save
/// is all or nothing, and a stale version is refused — and the contract
/// <c>IReminderRepository.GetDueAsync</c> documents. It does not prove that the SQL implementing
/// that contract orders and filters the same way; the integration suite covers the query, and runs
/// only where Docker does.
/// </para>
/// </summary>
public sealed class FireDueRemindersDeliveryTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly DateTimeOffset _start = StubDateTimeProvider.DefaultInstant;

    private readonly InMemoryReminderStore _store = new();
    private readonly ScriptedNotifier _notifier = new();
    private readonly ITodoItemQueries _todoItems = Substitute.For<ITodoItemQueries>();
    private readonly ILeaderLease _lease = Substitute.For<ILeaderLease>();

    public FireDueRemindersDeliveryTests()
    {
        // Every item is outstanding: this class is about delivery, not about what an item's state
        // does to a reminder, which FireDueRemindersUseCaseTests covers.
        _todoItems.GetCompletionStatesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<Guid, bool>)call.Arg<IReadOnlyList<Guid>>()
                .ToDictionary(id => id, _ => false));

        _lease.TryRunExclusivelyAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await call.Arg<Func<CancellationToken, Task>>()!(call.Arg<CancellationToken>());

                return true;
            });
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    // ---- One conflict must not resend what was already delivered -----------------------------------

    /// <summary>
    /// Three due reminders, and the middle one is cancelled by somebody else while its notification
    /// is in flight. The conflict may cost that one reminder; it must not cost the other two a second
    /// notification on the next pass.
    /// </summary>
    [Fact]
    public async Task AConflictOnOneReminder_DoesNotMakeTheNextPassResendTheOthers()
    {
        var first = Seed(minutesOverdue: 3);
        var second = Seed(minutesOverdue: 2);
        var third = Seed(minutesOverdue: 1);
        _notifier.OnNotify = notification =>
        {
            if (notification.TodoItemId == second.TodoItemId)
            {
                _store.CancelBehindTheBack(second.Id);
            }
        };

        // What the job does with a failing pass: log it and try again at the next tick.
        await Record.ExceptionAsync(() => RunPassAsync(_start));
        await RunPassAsync(_start.AddMinutes(1));

        _notifier.CountFor(first).ShouldBe(1, "delivered in the first pass and committed, so never again");
        _notifier.CountFor(second).ShouldBe(1, "sent while it was being cancelled, and cancelled stays cancelled");
        _notifier.CountFor(third).ShouldBe(1, "delivered once, whichever pass reached it");
    }

    [Fact]
    public async Task AConflictOnOneReminder_IsNotAFailureOfThePass()
    {
        var only = Seed(minutesOverdue: 1);
        _notifier.OnNotify = _ => _store.CancelBehindTheBack(only.Id);

        var exception = await Record.ExceptionAsync(() => RunPassAsync(_start));

        exception.ShouldBeNull("a lost race on one reminder is that reminder's outcome, not an error of the pass");
    }

    [Fact]
    public async Task EachFiredReminder_IsCommittedOnItsOwn()
    {
        Seed(minutesOverdue: 3);
        Seed(minutesOverdue: 2);
        Seed(minutesOverdue: 1);

        await RunPassAsync(_start);

        _store.SaveAttempts.ShouldBe(3);
        _store.Writes.Count(write => write.State == ReminderState.Fired).ShouldBe(3);
    }

    // ---- A reminder that keeps failing must not keep the others out --------------------------------

    /// <summary>
    /// More reminders that always fail than one batch holds, all due before a healthy one. With the
    /// failures staying first in due order, the healthy reminder is never reached: the batch is full
    /// of them on every pass, for ever.
    /// </summary>
    [Fact]
    public async Task RemindersThatAlwaysFail_DoNotKeepALaterOneOutOfTheBatch()
    {
        var failing = Enumerable.Range(0, 1000).Select(index => Seed(minutesOverdue: 2000 - index)).ToList();
        var healthy = Seed(minutesOverdue: 1);
        _notifier.FailFor.UnionWith(failing.Select(reminder => reminder.TodoItemId));

        await RunPassAsync(_start);

        int batchSize = _store.LastRequestedBatchSize;
        batchSize.ShouldBeLessThan(failing.Count, "the scenario needs a backlog bigger than one batch");

        // Everything that was waiting ahead of the healthy reminder gets one turn first, so it is
        // reached within as many passes as there are batches ahead of it, and not later.
        int passes = (failing.Count / batchSize) + 1;

        for (int pass = 1; pass <= passes && _notifier.CountFor(healthy) == 0; pass++)
        {
            await RunPassAsync(_start.AddMinutes(pass));
        }

        _notifier.CountFor(healthy).ShouldBe(1, "a healthy reminder is delivered once its turn comes round");
    }

    [Fact]
    public async Task AFailedReminder_IsNotRetriedBeforeItsWaitHasElapsed()
    {
        var reminder = Seed(minutesOverdue: 1);
        _notifier.FailFor.Add(reminder.TodoItemId);

        await RunPassAsync(_start);
        await RunPassAsync(_start.AddSeconds(30));

        _notifier.CountFor(reminder).ShouldBe(1, "30 seconds after a failure is inside the first wait");

        await RunPassAsync(_start.AddMinutes(1));

        _notifier.CountFor(reminder).ShouldBe(2, "the first wait is a minute");
    }

    [Fact]
    public async Task AReminderThatFailsThenRecovers_IsDeliveredExactlyOnce()
    {
        var reminder = Seed(minutesOverdue: 1);
        _notifier.FailFor.Add(reminder.TodoItemId);

        await RunPassAsync(_start);

        _notifier.FailFor.Clear();
        await RunPassAsync(_start.AddMinutes(1));
        await RunPassAsync(_start.AddMinutes(2));
        await RunPassAsync(_start.AddMinutes(10));

        _notifier.SuccessesFor(reminder).ShouldBe(1);
        _store.Committed(reminder.Id).State.ShouldBe(ReminderState.Fired);
    }

    private Reminder Seed(int minutesOverdue)
    {
        var reminder = AReminder.Rehydrated(_ownerId, dueAt: _start.AddMinutes(-minutesOverdue));
        _store.Seed(reminder);

        return reminder;
    }

    private async Task RunPassAsync(DateTimeOffset now)
    {
        // A pass is a scope of its own: nothing a previous one loaded or left staged is visible.
        _store.StartNewScope();

        var useCase = new FireDueRemindersUseCase(
            _store,
            _todoItems,
            _notifier,
            Recipients(),
            Substitute.For<IReminderDiagnostics>(),
            _store,
            new StubDateTimeProvider(now),
            _lease,
            NullLogger<FireDueRemindersUseCase>.Instance);

        await useCase.ExecuteAsync(TestToken);
    }

    private static IReminderRecipientDirectory Recipients()
    {
        var recipients = Substitute.For<IReminderRecipientDirectory>();
        recipients.FindAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(new ReminderRecipient("someone", "someone@example.com"));

        return recipients;
    }

    /// <summary>Records every notification, and fails or reacts for the reminders a test names.</summary>
    private sealed class ScriptedNotifier : IReminderNotifier
    {
        private readonly List<(Guid TodoItemId, bool Succeeded)> _attempts = [];

        public HashSet<Guid> FailFor { get; } = [];

        public Action<ReminderNotification>? OnNotify { get; set; }

        public Task NotifyAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
        {
            OnNotify?.Invoke(notification);

            bool fails = FailFor.Contains(notification.TodoItemId);
            _attempts.Add((notification.TodoItemId, !fails));

            return fails
                ? Task.FromException(new InvalidOperationException("the relay refused this one"))
                : Task.CompletedTask;
        }

        public int CountFor(Reminder reminder) => _attempts.Count(attempt => attempt.TodoItemId == reminder.TodoItemId);

        public int SuccessesFor(Reminder reminder) =>
            _attempts.Count(attempt => attempt.TodoItemId == reminder.TodoItemId && attempt.Succeeded);
    }
}
