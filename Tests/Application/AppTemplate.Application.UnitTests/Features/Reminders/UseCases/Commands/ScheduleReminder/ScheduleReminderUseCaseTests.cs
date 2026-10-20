using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.ScheduleReminder;

public sealed class ScheduleReminderUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly ITodoItemQueries _todoItems = Substitute.For<ITodoItemQueries>();
    private readonly IReminderRepository _reminders = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ScheduleReminderCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(
            new ScheduleReminderCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), _now.AddHours(1)),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(
            new ScheduleReminderCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), _now.AddHours(1)),
            TestToken);

        await _todoItems.DidNotReceive().ExistsForOwnerAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>());
        _reminders.DidNotReceive().Add(Arg.Any<Reminder>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyListId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(Guid.Empty, Guid.CreateVersion7(), _now.AddHours(1)), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["todoListId"].ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AnEmptyItemId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(Guid.CreateVersion7(), Guid.Empty, _now.AddHours(1)), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["todoItemId"].ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ANullCommand_IsAProgrammingError() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    [Fact]
    public async Task AnItemTheCallerCannotSee_IsReportedAsTargetNotFound()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        _todoItems.ExistsForOwnerAsync(listId, itemId, _callerId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(listId, itemId, _now.AddHours(1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("reminder.targetNotFound");
    }

    [Fact]
    public async Task ATargetNotFound_DoesNotCommit()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        _todoItems.ExistsForOwnerAsync(listId, itemId, _callerId, Arg.Any<CancellationToken>())
            .Returns(false);

        await UseCase().ExecuteAsync(new ScheduleReminderCommand(listId, itemId, _now.AddHours(1)), TestToken);

        _reminders.DidNotReceive().Add(Arg.Any<Reminder>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADueDateInThePast_IsReportedAsAConflict()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        GivenTheListHasTheItem(listId, itemId);

        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(listId, itemId, _now.AddHours(-1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("domain.invariantViolated");
        _reminders.DidNotReceive().Add(Arg.Any<Reminder>());
    }

    [Fact]
    public async Task AValidCommand_SchedulesAndCommits()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        GivenTheListHasTheItem(listId, itemId);

        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(listId, itemId, _now.AddHours(1)), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.TodoListId.ShouldBe(listId);
        result.Value.Value.TodoItemId.ShouldBe(itemId);
        result.Value.Value.DueAt.ShouldBe(_now.AddHours(1));
        _reminders.Received(1).Add(Arg.Any<Reminder>());
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    private ScheduleReminderUseCase UseCaseFor(ICurrentUser currentUser) => new(
        _todoItems,
        _reminders,
        _unitOfWork,
        currentUser,
        new StubDateTimeProvider(),
        _validator);

    private ScheduleReminderUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private void GivenTheListHasTheItem(Guid listId, Guid itemId) =>
        _todoItems.ExistsForOwnerAsync(listId, itemId, _callerId, Arg.Any<CancellationToken>())
            .Returns(true);
}
