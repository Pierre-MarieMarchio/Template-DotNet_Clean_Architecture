using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Reminders.Consumers.TodoItemCompleted;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using AppTemplate.Domain.Features.TodoLists.Events;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.Consumers.TodoItemCompleted;

public sealed class CancelRemindersOnTodoItemCompletedConsumerTests
{
    private static readonly Guid _ownerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APendingReminderOnTheCompletedItem_IsCancelledAndCommitted()
    {
        var todoItemId = Guid.CreateVersion7();
        var reminder = AReminder.OwnedBy(_ownerId, todoItemId: todoItemId);
        _repository.GetForTodoItemAsync(todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[reminder]);

        await Consumer().ConsumeAsync(ACompletionOf(todoItemId), TestToken);

        reminder.State.ShouldBe(ReminderState.Cancelled);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    /// <summary>A fired reminder is left alone: cancelling it would throw, and it fired for the
    /// same reason it should not fire again — it already did.</summary>
    [Fact]
    public async Task AnAlreadyFiredReminder_IsLeftAlone()
    {
        var todoItemId = Guid.CreateVersion7();
        var fired = AReminder.Rehydrated(_ownerId, _now, ReminderState.Fired, notifiedAt: _now, todoItemId: todoItemId);
        _repository.GetForTodoItemAsync(todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[fired]);

        await Consumer().ConsumeAsync(ACompletionOf(todoItemId), TestToken);

        fired.State.ShouldBe(ReminderState.Fired);
    }

    /// <summary>Nothing left to cancel means nothing to save either — proof that a redelivery of
    /// this event, after the first delivery already cancelled everything, is a true no-op.</summary>
    [Fact]
    public async Task NoPendingReminders_DoesNotCommit()
    {
        var todoItemId = Guid.CreateVersion7();
        var cancelled = AReminder.Rehydrated(_ownerId, _now, ReminderState.Cancelled, todoItemId: todoItemId);
        _repository.GetForTodoItemAsync(todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[cancelled]);

        await Consumer().ConsumeAsync(ACompletionOf(todoItemId), TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultiplePendingReminders_AreAllCancelled()
    {
        var todoItemId = Guid.CreateVersion7();
        var first = AReminder.OwnedBy(_ownerId, todoItemId: todoItemId);
        var second = AReminder.OwnedBy(_ownerId, todoItemId: todoItemId);
        _repository.GetForTodoItemAsync(todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[first, second]);

        await Consumer().ConsumeAsync(ACompletionOf(todoItemId), TestToken);

        first.State.ShouldBe(ReminderState.Cancelled);
        second.State.ShouldBe(ReminderState.Cancelled);
    }

    [Fact]
    public async Task ANullEvent_IsAProgrammingError() =>
        await Should.ThrowAsync<ArgumentNullException>(() => Consumer().ConsumeAsync(null!, TestToken));

    private CancelRemindersOnTodoItemCompletedConsumer Consumer() => new(_repository, _unitOfWork);

    private static TodoItemCompletedDomainEvent ACompletionOf(Guid todoItemId) =>
        new(Guid.CreateVersion7(), todoItemId, "Buy milk", _now);
}
