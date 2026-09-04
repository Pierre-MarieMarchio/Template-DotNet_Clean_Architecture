using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.Validators;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Domain.Features.TodoLists.Stores;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands;

public sealed class CompleteTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new CompleteTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new CompleteTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ownership

    [Fact]
    public async Task AMissingList_IsReportedAsNotFoundRatherThanThrown()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(missingId, Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ownership check. Deleting the <c>OwnerId</c> comparison lets any authenticated
    /// caller complete items on any list, and turns this red.
    /// </summary>
    [Fact]
    public async Task AnItemOnAnotherUsersList_IsNotCompleted()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        var foreignItemId = foreign.AddItem("Not mine", null);
        foreign.ClearDomainEvents();
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(foreign.Id, foreignItemId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        foreign.Items.ShouldHaveSingleItem().IsCompleted.ShouldBeFalse();
        foreign.DomainEvents.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The list-level failure is reported as "no such list", never as "no such item": the
    /// latter would confirm that the list exists and belongs to somebody.
    /// </summary>
    [Fact]
    public async Task AnotherUsersList_IsReportedAsAMissingListNotAMissingItem()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        var foreignItemId = foreign.AddItem("Not mine", null);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(foreign.Id, foreignItemId), TestToken);

        result.Error!.Code.ShouldBe("todoList.notFound");
    }

    #endregion

    #region Missing item

    /// <summary>
    /// The aggregate throws on an unknown item id, but asking for something that is not
    /// there is an expected outcome of a request, so it is answered as a result.
    /// </summary>
    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFoundRatherThanThrown()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoItem.notFound");
    }

    [Fact]
    public async Task AnUnknownItemId_DoesNotCommit()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, Guid.CreateVersion7()), TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnItemIdFromAnotherList_IsReportedAsAMissingItem()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        var otherList = ATodoList.OwnedByWithItem(_callerId, out var otherItemId, "Elsewhere");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, otherItemId), TestToken);

        result.Error!.Code.ShouldBe("todoItem.notFound");
        otherList.Items.ShouldHaveSingleItem().IsCompleted.ShouldBeFalse();
    }

    #endregion

    #region Invariants surfaced as conflicts

    /// <summary>
    /// Completing something already completed depends on current state, not on the shape of
    /// the request, so it is a conflict — and it must not escape as an exception.
    /// </summary>
    [Fact]
    public async Task AnAlreadyCompletedItem_BecomesAConflict()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        var firstCompletion = FixedDateTimeProvider.DefaultInstant.AddDays(-1);
        list.CompleteItem(itemId, firstCompletion);
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("todoList.invariantViolated");
    }

    [Fact]
    public async Task AnAlreadyCompletedItem_KeepsItsOriginalCompletionTimeAndDoesNotCommit()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        var firstCompletion = FixedDateTimeProvider.DefaultInstant.AddDays(-1);
        list.CompleteItem(itemId, firstCompletion);
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        list.Items.ShouldHaveSingleItem().CompletedAt.ShouldBe(firstCompletion);
        list.DomainEvents.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Success

    [Fact]
    public async Task TheItem_IsCompletedAtTheInjectedClocksInstant()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var item = list.Items.ShouldHaveSingleItem();
        item.IsCompleted.ShouldBeTrue();
        item.CompletedAt.ShouldBe(FixedDateTimeProvider.DefaultInstant);
    }

    /// <summary>
    /// The clock is injected, so a use case cannot smuggle in an ambient
    /// <c>DateTime.UtcNow</c> that a test could not pin down.
    /// </summary>
    [Fact]
    public async Task TheCompletionInstant_ComesFromTheProviderAndNotTheSystemClock()
    {
        var pinnedInstant = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var useCase = new CompleteTodoItemUseCase(
            _repository,
            _unitOfWork,
            StubCurrentUser.WithId(_callerId),
            new FixedDateTimeProvider(pinnedInstant));

        await useCase.ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        list.Items.ShouldHaveSingleItem().CompletedAt.ShouldBe(pinnedInstant);
    }

    [Fact]
    public async Task ASuccessfulCompletion_RaisesTheCompletionEvent()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        var domainEvent = list.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<TodoItemCompletedDomainEvent>();
        domainEvent.TodoItemId.ShouldBe(itemId);
        domainEvent.TodoListId.ShouldBe(list.Id);
    }

    [Fact]
    public async Task ASuccessfulCompletion_CommitsExactlyOnce()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyTheRequestedItem_IsCompleted()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        var untouchedId = list.AddItem("Leave me alone", null);
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), TestToken);

        list.Items.Single(item => item.Id == itemId).IsCompleted.ShouldBeTrue();
        list.Items.Single(item => item.Id == untouchedId).IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheLoadAndTheCommit()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId), cancellation.Token);

        await _repository.Received(1).GetAsync(list.Id, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    #endregion

    private CompleteTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_repository, _unitOfWork, currentUser, _clock);

    private CompleteTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
