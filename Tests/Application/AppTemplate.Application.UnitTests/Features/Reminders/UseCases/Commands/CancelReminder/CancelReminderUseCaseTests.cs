using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Services;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed class CancelReminderUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CancelReminderCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new CancelReminderCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    #endregion

    #region Validation

    [Fact]
    public async Task AnEmptyReminderId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(Guid.Empty), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ANullCommand_IsAProgrammingError() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

    #region Ownership

    [Fact]
    public async Task AMissingReminder_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((Reminder?)null);

        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(missingId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("reminder.notFound");
    }

    [Fact]
    public async Task AnotherUsersReminder_IsNotCancelled()
    {
        var foreign = AReminder.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        foreign.State.ShouldBe(ReminderState.Pending);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task AnUnsatisfiedPrecondition_Fails()
    {
        var reminder = GivenTheCallerOwnsAReminder();
        var precondition = new VersionPrecondition([reminder.Version + 1]);

        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(reminder.Id, precondition), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    #endregion

    #region Domain

    [Fact]
    public async Task AnAlreadyFiredReminder_CannotBeCancelled()
    {
        var reminder = AReminder.Rehydrated(
            _callerId, StubDateTimeProvider.DefaultInstant, ReminderState.Fired, notifiedAt: StubDateTimeProvider.DefaultInstant);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(reminder.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Idempotent by shape: cancelling twice is a no-op, not an error.</summary>
    [Fact]
    public async Task AnAlreadyCancelledReminder_CancelsAgainWithoutError()
    {
        var reminder = AReminder.Rehydrated(
            _callerId, StubDateTimeProvider.DefaultInstant, ReminderState.Cancelled);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(reminder.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        reminder.State.ShouldBe(ReminderState.Cancelled);
    }

    #endregion

    #region Success

    [Fact]
    public async Task AValidCancel_ChangesTheStateAndCommits()
    {
        var reminder = GivenTheCallerOwnsAReminder();

        var result = await UseCase().ExecuteAsync(new CancelReminderCommand(reminder.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        reminder.State.ShouldBe(ReminderState.Cancelled);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    #endregion

    private CancelReminderUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new ReminderAccess(_repository, currentUser), _unitOfWork, _validator);

    private CancelReminderUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private Reminder GivenTheCallerOwnsAReminder()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        return reminder;
    }
}
