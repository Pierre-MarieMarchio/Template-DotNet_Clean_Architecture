using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Concurrency;

/// <summary>
/// The precondition itself, and the rule that every operation which changes the aggregate honours it.
/// </summary>
/// <remarks>
/// The enforcement cases are a theory over the five mutating use cases rather than five pairs of
/// tests scattered across five files. The check is two lines of identical code in each of them, and
/// the failure worth catching is one of them not having it — which a per-use-case test only catches
/// if somebody remembers to write it.
/// </remarks>
public sealed class VersionPreconditionTests
{
    private const uint _storedVersion = 4242;
    private const uint _staleVersion = 17;

    private const string _rename = "rename the list";
    private const string _delete = "delete the list";
    private const string _addItem = "add an item";
    private const string _completeItem = "complete an item";
    private const string _removeItem = "remove an item";

    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public static TheoryData<string> MutatingOperations =>
        [_rename, _delete, _addItem, _completeItem, _removeItem];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region The precondition itself

    [Fact]
    public void AVersionInTheSet_SatisfiesThePrecondition() =>
        new VersionPrecondition([1, 2, 3]).IsSatisfiedBy(2).ShouldBeTrue();

    [Fact]
    public void AVersionOutsideTheSet_DoesNot() =>
        new VersionPrecondition([1, 2, 3]).IsSatisfiedBy(4).ShouldBeFalse();

    /// <summary>
    /// The direction that matters. An empty set is what a caller naming a validator this
    /// application never issued produces, and treating it as "no constraint" would turn every
    /// unusable entity tag into an unconditional write.
    /// </summary>
    [Fact]
    public void AnEmptySet_SatisfiesNothing()
    {
        var precondition = new VersionPrecondition([]);

        precondition.IsSatisfiedBy(0).ShouldBeFalse();
        precondition.IsSatisfiedBy(_storedVersion).ShouldBeFalse();
    }

    #endregion

    #region Enforcement

    [Theory]
    [MemberData(nameof(MutatingOperations))]
    public async Task AnOperationDecidedAgainstAnOlderVersion_IsRefused(string operation)
    {
        var list = GivenTheCallerOwnsAListAtTheStoredVersion(out var itemId);

        var result = await ExecuteAsync(operation, list, itemId, new VersionPrecondition([_staleVersion]));

        result.IsFailure.ShouldBeTrue(operation);

        var error = result.Error;

        error.ShouldNotBeNull(operation);
        error.Type.ShouldBe(ErrorType.PreconditionFailed, operation);
        error.Code.ShouldBe("precondition.failed", operation);
    }

    /// <summary>
    /// The half that makes the refusal mean something: a refused operation must not have been
    /// applied. A use case that returned the failure after committing would pass the test above.
    /// </summary>
    [Theory]
    [MemberData(nameof(MutatingOperations))]
    public async Task ARefusedOperation_CommitsNothing(string operation)
    {
        var list = GivenTheCallerOwnsAListAtTheStoredVersion(out var itemId);

        await ExecuteAsync(operation, list, itemId, new VersionPrecondition([_staleVersion]));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Remove(Arg.Any<TodoList>());
    }

    [Theory]
    [MemberData(nameof(MutatingOperations))]
    public async Task AnOperationDecidedAgainstTheStoredVersion_Proceeds(string operation)
    {
        var list = GivenTheCallerOwnsAListAtTheStoredVersion(out var itemId);

        var result = await ExecuteAsync(operation, list, itemId, new VersionPrecondition([_storedVersion]));

        result.IsSuccess.ShouldBeTrue(operation);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A caller may name several versions, and one of them matching is enough — that is what
    /// <c>If-Match</c>'s list form means.
    /// </summary>
    [Theory]
    [MemberData(nameof(MutatingOperations))]
    public async Task OneAcceptableVersionAmongSeveral_IsEnough(string operation)
    {
        var list = GivenTheCallerOwnsAListAtTheStoredVersion(out var itemId);

        var result = await ExecuteAsync(
            operation,
            list,
            itemId,
            new VersionPrecondition([_staleVersion, _storedVersion]));

        result.IsSuccess.ShouldBeTrue(operation);
    }

    /// <summary>
    /// No precondition is not a failed one: an unconditional write stays the behaviour a caller
    /// that sends no <c>If-Match</c> gets.
    /// </summary>
    [Theory]
    [MemberData(nameof(MutatingOperations))]
    public async Task NoPrecondition_LeavesTheOperationUnconditional(string operation)
    {
        var list = GivenTheCallerOwnsAListAtTheStoredVersion(out var itemId);

        var result = await ExecuteAsync(operation, list, itemId, precondition: null);

        result.IsSuccess.ShouldBeTrue(operation);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    private static async Task<Result> Widened<TValue>(Task<Result<TValue>> execution) where TValue : notnull =>
        await execution;

    private TodoList GivenTheCallerOwnsAListAtTheStoredVersion(out Guid itemId)
    {
        var list = ATodoList.OwnedByWithItemAtVersion(_callerId, _storedVersion, out itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        return list;
    }

    private Task<Result> ExecuteAsync(
        string operation,
        TodoList list,
        Guid itemId,
        VersionPrecondition? precondition)
    {
        var currentUser = StubCurrentUser.WithId(_callerId);
        var access = new TodoListAccess(_repository, currentUser);

        return operation switch
        {
            _rename => Widened(
                new RenameTodoListUseCase(access, _unitOfWork, new RenameTodoListCommandValidator())
                    .ExecuteAsync(new RenameTodoListCommand(list.Id, "Renamed", precondition), TestToken)),

            _delete => new DeleteTodoListUseCase(
                    access,
                    _repository,
                    _unitOfWork,
                    new DeleteTodoListCommandValidator())
                .ExecuteAsync(new DeleteTodoListCommand(list.Id, precondition), TestToken),

            _addItem => Widened(
                new AddTodoItemUseCase(access, _unitOfWork, new AddTodoItemCommandValidator())
                    .ExecuteAsync(
                        new AddTodoItemCommand(list.Id, "A second item", null, null, precondition),
                        TestToken)),

            _completeItem => Widened(
                new CompleteTodoItemUseCase(
                        access,
                        _unitOfWork,
                        new FixedDateTimeProvider(),
                        new CompleteTodoItemCommandValidator())
                    .ExecuteAsync(new CompleteTodoItemCommand(list.Id, itemId, precondition), TestToken)),

            _removeItem => Widened(
                new RemoveTodoItemUseCase(access, _unitOfWork, new RemoveTodoItemCommandValidator())
                    .ExecuteAsync(new RemoveTodoItemCommand(list.Id, itemId, precondition), TestToken)),

            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };
    }
}
