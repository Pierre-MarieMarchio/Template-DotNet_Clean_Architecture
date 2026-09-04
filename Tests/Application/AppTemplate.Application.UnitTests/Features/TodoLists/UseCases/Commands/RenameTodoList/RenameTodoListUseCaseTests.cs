using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.RenameTodoList;

public sealed class RenameTodoListUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RenameTodoListCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RenameTodoListCommand(Guid.CreateVersion7(), "Shopping"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    /// <summary>
    /// An anonymous caller must not even reach the repository: a use case that loads first
    /// and authorises later has already read data on behalf of nobody.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_ReadsAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RenameTodoListCommand(Guid.CreateVersion7(), "Shopping"), TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankName_IsRejected(string name)
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(new RenameTodoListCommand(list.Id, name), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        list.Name.Value.ShouldBe("Groceries");
    }

    [Fact]
    public async Task ANameLongerThanTheDomainAllows_IsRejected()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(
            new RenameTodoListCommand(list.Id, new string('a', TodoListName.MaxLength + 1)),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        list.Name.Value.ShouldBe("Groceries");
    }

    [Fact]
    public async Task AnEmptyListId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(new RenameTodoListCommand(Guid.Empty, "Shopping"), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["todoListId"].Any(message => message.Contains("list id", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task AnInvalidCommand_DoesNotCommit()
    {
        GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(new RenameTodoListCommand(Guid.Empty, ""), TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANullCommand_IsAProgrammingError() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

    #region Ownership

    [Fact]
    public async Task AMissingList_IsReportedAsNotFoundRatherThanThrown()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var result = await UseCase().ExecuteAsync(new RenameTodoListCommand(missingId, "Shopping"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
    }

    /// <summary>
    /// The ownership check. Deleting the <c>OwnerId</c> comparison lets any authenticated
    /// caller rename any list in the database, and turns this red.
    /// </summary>
    [Fact]
    public async Task AnotherUsersList_IsNotRenamed()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new RenameTodoListCommand(foreign.Id, "Mine now"), TestToken);

        result.IsFailure.ShouldBeTrue();
        foreign.Name.Value.ShouldBe("Somebody else's list");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// "Not yours" and "does not exist" are answered identically, so the two cannot be
    /// told apart by a caller probing for other users' list ids.
    /// </summary>
    [Fact]
    public async Task AnotherUsersList_IsIndistinguishableFromAMissingOne()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var foreignResult = await UseCase().ExecuteAsync(new RenameTodoListCommand(foreign.Id, "Mine now"), TestToken);
        var missingResult = await UseCase().ExecuteAsync(new RenameTodoListCommand(missingId, "Mine now"), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    #endregion

    #region Success

    [Fact]
    public async Task AValidRename_ReplacesTheName()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(new RenameTodoListCommand(list.Id, "Shopping"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        list.Name.Value.ShouldBe("Shopping");
    }

    [Fact]
    public async Task TheNewName_IsNormalisedByTheDomain()
    {
        var list = GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(new RenameTodoListCommand(list.Id, "  Shopping  "), TestToken);

        list.Name.Value.ShouldBe("Shopping");
    }

    [Fact]
    public async Task ASuccessfulRename_CommitsExactlyOnce()
    {
        var list = GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(new RenameTodoListCommand(list.Id, "Shopping"), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheLoadAndTheCommit()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new RenameTodoListCommand(list.Id, "Shopping"), cancellation.Token);

        await _repository.Received(1).GetAsync(list.Id, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Fact]
    public async Task ARename_RaisesNoDomainEvent()
    {
        var list = GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(new RenameTodoListCommand(list.Id, "Shopping"), TestToken);

        list.DomainEvents.ShouldBeEmpty();
    }

    #endregion

    private RenameTodoListUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListService(_repository, currentUser), _unitOfWork, _validator);

    private RenameTodoListUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private TodoList GivenTheCallerOwnsAList()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        return list;
    }
}
