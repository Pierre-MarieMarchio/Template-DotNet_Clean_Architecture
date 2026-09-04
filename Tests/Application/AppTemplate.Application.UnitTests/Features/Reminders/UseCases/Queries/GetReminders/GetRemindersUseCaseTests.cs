using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.UseCases.Queries.GetReminders;

public sealed class GetRemindersUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _todoListId = Guid.CreateVersion7();
    private static readonly Guid _todoItemId = Guid.CreateVersion7();

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
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
    public async Task ANullQuery_IsAProgrammingError() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    /// <summary>
    /// <see cref="IReminderRepository.GetForTodoItemAsync"/> answers for every owner; hiding
    /// somebody else's reminder is this use case's job.
    /// </summary>
    [Fact]
    public async Task AnotherOwnersReminderOnTheSameItem_IsNotReturned()
    {
        var mine = AReminder.OwnedBy(_callerId, todoListId: _todoListId, todoItemId: _todoItemId);
        var foreign = AReminder.OwnedBySomebodyElseThan(_callerId);
        _repository.GetForTodoItemAsync(_todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[mine, foreign]);

        var result = await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
    }

    /// <summary>
    /// The list id in the route is checked, not decorative. Every other route into an item refuses
    /// to reach it through the wrong list, and an id a caller may fill in freely teaches them it
    /// does not matter.
    /// </summary>
    [Fact]
    public async Task AReminderReachedThroughADifferentList_IsNotReturned()
    {
        var reminder = AReminder.OwnedBy(_callerId, todoListId: _todoListId, todoItemId: _todoItemId);
        _repository.GetForTodoItemAsync(_todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[reminder]);

        var result = await UseCase()
            .ExecuteAsync(new GetRemindersQuery(Guid.CreateVersion7(), _todoItemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCallersOwnReminders_AreOrderedByDueDate()
    {
        var later = AReminder.OwnedBy(_callerId, dueAt: FixedDateTimeProvider.DefaultInstant.AddDays(2), todoListId: _todoListId, todoItemId: _todoItemId);
        var sooner = AReminder.OwnedBy(_callerId, dueAt: FixedDateTimeProvider.DefaultInstant.AddDays(1), todoListId: _todoListId, todoItemId: _todoItemId);
        _repository.GetForTodoItemAsync(_todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[later, sooner]);

        var result = await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        result.Value.Select(dto => dto.Id).ShouldBe([sooner.Id, later.Id]);
    }

    [Fact]
    public async Task NoReminders_ReturnsAnEmptyList()
    {
        _repository.GetForTodoItemAsync(_todoItemId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[]);

        var result = await UseCase().ExecuteAsync(new GetRemindersQuery(_todoListId, _todoItemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    private GetRemindersUseCase UseCaseFor(ICurrentUser currentUser) => new(_repository, currentUser, _validator);

    private GetRemindersUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
