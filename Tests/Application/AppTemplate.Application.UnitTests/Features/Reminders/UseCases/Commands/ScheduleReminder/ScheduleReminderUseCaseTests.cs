using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Commands.ScheduleReminder;

public sealed class ScheduleReminderUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = FixedDateTimeProvider.DefaultInstant;

    private readonly ITodoListQueries _todoLists = Substitute.For<ITodoListQueries>();
    private readonly IReminderRepository _reminders = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ScheduleReminderCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

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

        await _todoLists.DidNotReceive().GetDetailAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _reminders.DidNotReceive().Add(Arg.Any<Reminder>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Validation

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

    #endregion

    #region Target

    [Fact]
    public async Task AListTheCallerDoesNotOwn_IsReportedAsTargetNotFound()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        _todoLists.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(listId, itemId, _now.AddHours(1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("reminder.targetNotFound");
    }

    [Fact]
    public async Task AnItemNotOnTheNamedList_IsReportedAsTargetNotFound()
    {
        var listId = Guid.CreateVersion7();
        GivenTheListHasAnItemOtherThan(listId, out var itemId);

        var result = await UseCase().ExecuteAsync(
            new ScheduleReminderCommand(listId, itemId, _now.AddHours(1)), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("reminder.targetNotFound");
    }

    [Fact]
    public async Task ATargetNotFound_DoesNotCommit()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        _todoLists.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        await UseCase().ExecuteAsync(new ScheduleReminderCommand(listId, itemId, _now.AddHours(1)), TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Domain

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

    #endregion

    #region Success

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

    #endregion

    private ScheduleReminderUseCase UseCaseFor(ICurrentUser currentUser) => new(
        _todoLists,
        _reminders,
        _unitOfWork,
        currentUser,
        new FixedDateTimeProvider(),
        _validator);

    private ScheduleReminderUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private void GivenTheListHasTheItem(Guid listId, Guid itemId)
    {
        var item = new TodoItemDto(itemId, "Buy milk", null, false, null, []);
        var detail = new TodoListDetailDto(listId, "Groceries", _now, null, [item]);
        _todoLists.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new Versioned<TodoListDetailDto>(detail, 3));
    }

    private void GivenTheListHasAnItemOtherThan(Guid listId, out Guid missingItemId)
    {
        missingItemId = Guid.CreateVersion7();
        var otherItem = new TodoItemDto(Guid.CreateVersion7(), "Buy milk", null, false, null, []);
        var detail = new TodoListDetailDto(listId, "Groceries", _now, null, [otherItem]);
        _todoLists.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new Versioned<TodoListDetailDto>(detail, 3));
    }
}
