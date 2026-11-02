using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Application.Features.Reminders.Model.Mapping;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Queries.GetReminders;

public sealed class GetRemindersUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());
    private static readonly Guid _todoListId = Guid.CreateVersion7();
    private static readonly Guid _todoItemId = Guid.CreateVersion7();

    private readonly IReminderQueries _queries = Substitute.For<IReminderQueries>();
    private readonly GetRemindersQueryValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnEmptyItemId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, Guid.Empty), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task TheQuery_IsAskedForTheCallersOwnRemindersOnTheNamedItem()
    {
        _queries.GetForTodoItemAsync(_callerId, _todoListId, _todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ReminderDto>)[]);

        await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        await _queries.Received(1).GetForTodoItemAsync(_callerId, _todoListId, _todoItemId, TestToken);
    }

    [Fact]
    public async Task WhatTheQueryReturns_ComesBackUnchangedAndInTheSameOrder()
    {
        var sooner = ADto(StubDateTimeProvider.DefaultInstant.AddDays(1));
        var later = ADto(StubDateTimeProvider.DefaultInstant.AddDays(2));
        _queries.GetForTodoItemAsync(_callerId, _todoListId, _todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ReminderDto>)[sooner, later]);

        var result = await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([sooner, later]);
    }

    [Fact]
    public async Task NoReminders_ReturnsAnEmptyList()
    {
        _queries.GetForTodoItemAsync(_callerId, _todoListId, _todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ReminderDto>)[]);

        var result = await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        await _queries.DidNotReceive().GetForTodoItemAsync(
            Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static ReminderDto ADto(DateTimeOffset dueAt) =>
        ReminderDtoMapping.ToDto(AReminder.OwnedBy(_callerId, dueAt, _todoListId, _todoItemId));

    private GetRemindersUseCase UseCaseFor(ICurrentUser currentUser) => new(_queries, currentUser, _validator);

    private GetRemindersUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
