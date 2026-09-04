using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public sealed class DeleteTodoListUseCaseTests
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
            .ExecuteAsync(new DeleteTodoListCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_DeletesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new DeleteTodoListCommand(Guid.CreateVersion7()), TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Remove(Arg.Any<TodoList>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ownership

    [Fact]
    public async Task AMissingList_IsReportedAsNotFoundRatherThanThrown()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var result = await UseCase().ExecuteAsync(new DeleteTodoListCommand(missingId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
    }

    [Fact]
    public async Task AMissingList_IsNotDeletedAndDoesNotCommit()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        await UseCase().ExecuteAsync(new DeleteTodoListCommand(missingId), TestToken);

        _repository.DidNotReceive().Remove(Arg.Any<TodoList>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ownership check on the most destructive operation in the module. Deleting the
    /// <c>OwnerId</c> comparison lets any authenticated caller delete any list, and turns
    /// this red.
    /// </summary>
    [Fact]
    public async Task AnotherUsersList_IsNotDeleted()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new DeleteTodoListCommand(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        _repository.DidNotReceive().Remove(Arg.Any<TodoList>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherUsersList_IsIndistinguishableFromAMissingOne()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var foreignResult = await UseCase().ExecuteAsync(new DeleteTodoListCommand(foreign.Id), TestToken);
        var missingResult = await UseCase().ExecuteAsync(new DeleteTodoListCommand(missingId), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
        foreignResult.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    #endregion

    #region Success

    [Fact]
    public async Task TheCallersOwnList_IsStagedForDeletion()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(new DeleteTodoListCommand(list.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Remove(list);
    }

    /// <summary>
    /// The aggregate handed to <c>Remove</c> is the one that was loaded, not a detached
    /// stand-in built from the id — deleting a different instance would not cascade to the
    /// items and tags the aggregate owns.
    /// </summary>
    [Fact]
    public async Task TheAggregateThatWasLoaded_IsTheOneRemoved()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(new DeleteTodoListCommand(list.Id), TestToken);

        _repository.Received(1).Remove(Arg.Is<TodoList>(removed => ReferenceEquals(removed, list)));
    }

    [Fact]
    public async Task ASuccessfulDelete_CommitsExactlyOnce()
    {
        var list = GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(new DeleteTodoListCommand(list.Id), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheLoadAndTheCommit()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new DeleteTodoListCommand(list.Id), cancellation.Token);

        await _repository.Received(1).GetAsync(list.Id, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    #endregion

    private DeleteTodoListUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _repository, _unitOfWork, new DeleteTodoListCommandValidator());

    private DeleteTodoListUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private TodoList GivenTheCallerOwnsAList()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        return list;
    }
}
