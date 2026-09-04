using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.RemoveTodoItem;

public sealed class RemoveTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RemoveTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RemoveTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

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

        var result = await UseCase().ExecuteAsync(new RemoveTodoItemCommand(missingId, Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ownership check. Deleting the <c>OwnerId</c> comparison lets any authenticated
    /// caller delete items from any list, and turns this red.
    /// </summary>
    [Fact]
    public async Task AnItemOnAnotherUsersList_IsNotRemoved()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        var foreignItemId = foreign.AddItem("Not mine", null);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new RemoveTodoItemCommand(foreign.Id, foreignItemId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("todoList.notFound");
        foreign.Items.Count.ShouldBe(1);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Missing item

    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFoundRatherThanThrown()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoItem.notFound");
    }

    /// <summary>
    /// A removal that matched nothing must not commit: committing would report success for
    /// a change that never happened.
    /// </summary>
    [Fact]
    public async Task AnUnknownItemId_LeavesTheListIntactAndDoesNotCommit()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, Guid.CreateVersion7()), TestToken);

        list.Items.ShouldHaveSingleItem().Id.ShouldBe(itemId);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnItemIdFromAnotherList_IsReportedAsAMissingItem()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        var otherList = ATodoList.OwnedByWithItem(_callerId, out var otherItemId, "Elsewhere");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, otherItemId), TestToken);

        result.Error!.Code.ShouldBe("todoItem.notFound");
        otherList.Items.Count.ShouldBe(1);
    }

    #endregion

    #region Success

    [Fact]
    public async Task TheRequestedItem_IsRemoved()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        list.Items.ShouldBeEmpty();

        // The item is gone; what is returned is the list it used to belong to, at its new version.
        result.Value.Value.Id.ShouldBe(list.Id);
        result.Value.Value.Items.ShouldBeEmpty();
        result.Value.Version.ShouldBe(list.Version);
    }

    [Fact]
    public async Task OnlyTheRequestedItem_IsRemoved()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        var keptId = list.AddItem("Keep me", null);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId), TestToken);

        list.Items.ShouldHaveSingleItem().Id.ShouldBe(keptId);
    }

    [Fact]
    public async Task ASuccessfulRemoval_CommitsExactlyOnce()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The list is not removed along with the item — only the aggregate's contents change,
    /// and the aggregate itself stays staged for update.
    /// </summary>
    [Fact]
    public async Task ARemovalDoesNotDeleteTheList()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId), TestToken);

        _repository.DidNotReceive().Remove(Arg.Any<TodoList>());
    }

    [Fact]
    public async Task ARemoval_RaisesNoDomainEvent()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId), TestToken);

        list.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheLoadAndTheCommit()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId), cancellation.Token);

        await _repository.Received(1).GetAsync(list.Id, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    #endregion

    private RemoveTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, new RemoveTodoItemCommandValidator());

    private RemoveTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
