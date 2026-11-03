using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Services;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed class CancelReminderTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CancelReminderCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .Handle(new CancelReminderCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnEmptyReminderId_IsRejected()
    {
        var result = await UseCase().Handle(new CancelReminderCommand(Guid.Empty), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AMissingReminder_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(_callerId, missingId, Arg.Any<CancellationToken>()).Returns((Reminder?)null);

        var result = await UseCase().Handle(new CancelReminderCommand(missingId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("reminder.notFound");
    }

    [Fact]
    public async Task AnUnsatisfiedPrecondition_Fails()
    {
        var reminder = GivenTheCallerOwnsAReminder();
        var precondition = new VersionPrecondition([reminder.Version + 1]);

        var result = await UseCase().Handle(new CancelReminderCommand(reminder.Id, precondition), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public async Task AnAlreadyFiredReminder_CannotBeCancelled()
    {
        var reminder = AReminder.Rehydrated(
            _callerId, TheClock.DefaultInstant, ReminderState.Fired, notifiedAt: TheClock.DefaultInstant);
        _repository.GetAsync(_callerId, reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await UseCase().Handle(new CancelReminderCommand(reminder.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAlreadyCancelledReminder_CancelsAgainWithoutError()
    {
        var reminder = AReminder.Rehydrated(
            _callerId, TheClock.DefaultInstant, ReminderState.Cancelled);
        _repository.GetAsync(_callerId, reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await UseCase().Handle(new CancelReminderCommand(reminder.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        reminder.State.ShouldBe(ReminderState.Cancelled);
    }

    [Fact]
    public async Task AValidCancel_ChangesTheStateAndCommits()
    {
        var reminder = GivenTheCallerOwnsAReminder();

        var result = await UseCase().Handle(new CancelReminderCommand(reminder.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        reminder.State.ShouldBe(ReminderState.Cancelled);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    private ValidatedHandler<CancelReminderCommand, Result> UseCaseFor(ICurrentUser currentUser) =>
        new(new CancelReminderCommandHandler(new ReminderService(_repository, currentUser), _unitOfWork).Handle, _validator);

    private ValidatedHandler<CancelReminderCommand, Result> UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private Reminder GivenTheCallerOwnsAReminder()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(_callerId, reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        return reminder;
    }
}
