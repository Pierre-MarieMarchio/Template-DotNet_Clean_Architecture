using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Services;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.RescheduleReminder;

public sealed class RescheduleReminderUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RescheduleReminderCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RescheduleReminderCommand(Guid.CreateVersion7(), _now.AddDays(1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    #endregion

    #region Validation

    [Fact]
    public async Task AnEmptyReminderId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(
            new RescheduleReminderCommand(Guid.Empty, _now.AddDays(1)), TestToken);

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

        var result = await UseCase().ExecuteAsync(
            new RescheduleReminderCommand(missingId, _now.AddDays(1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    #endregion

    #region Domain

    [Fact]
    public async Task ANewDueDateInThePast_IsReportedAsAConflict()
    {
        var reminder = GivenTheCallerOwnsAReminder();

        var result = await UseCase().ExecuteAsync(
            new RescheduleReminderCommand(reminder.Id, _now.AddHours(-1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAlreadyFiredReminder_CannotBeRescheduled()
    {
        var reminder = AReminder.Rehydrated(
            _callerId, _now, ReminderState.Fired, notifiedAt: _now);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await UseCase().ExecuteAsync(
            new RescheduleReminderCommand(reminder.Id, _now.AddDays(1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task AnUnsatisfiedPrecondition_Fails()
    {
        var reminder = GivenTheCallerOwnsAReminder();
        var precondition = new VersionPrecondition([reminder.Version + 1]);

        var result = await UseCase().ExecuteAsync(
            new RescheduleReminderCommand(reminder.Id, _now.AddDays(1), precondition), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    #endregion

    #region Success

    [Fact]
    public async Task AValidReschedule_MovesTheDueDateAndCommits()
    {
        var reminder = GivenTheCallerOwnsAReminder();
        var newDueAt = _now.AddDays(2);

        var result = await UseCase().ExecuteAsync(new RescheduleReminderCommand(reminder.Id, newDueAt), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.DueAt.ShouldBe(newDueAt);
        reminder.DueAt.ShouldBe(newDueAt);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    #endregion

    private RescheduleReminderUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new ReminderAccess(_repository, currentUser),
        _unitOfWork,
        new StubDateTimeProvider(),
        _validator);

    private RescheduleReminderUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private Reminder GivenTheCallerOwnsAReminder()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        return reminder;
    }
}
