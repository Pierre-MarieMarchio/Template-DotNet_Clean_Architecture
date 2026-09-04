using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.AddTodoItem;

public sealed class AddTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly AddTodoItemCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(ACommandFor(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ACommandFor(Guid.CreateVersion7()), TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTitle_IsRejected(string title)
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(ACommandFor(list.Id, title), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATitleLongerThanTheDomainAllows_IsRejected()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(
            ACommandFor(list.Id, new string('a', TodoItemTitle.MaxLength + 1)),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADescriptionLongerThanTheDomainAllows_IsRejected()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(
            new AddTodoItemCommand(list.Id, "Buy milk", new string('a', TodoItem.MaxDescriptionLength + 1), null),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyListId_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(ACommandFor(Guid.Empty), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["todoListId"].Any(message => message.Contains("list id", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    /// <summary>
    /// A tag the domain would refuse is caught by the validator first, so the caller gets a
    /// 400-shaped answer rather than a 409 about an invariant they could have avoided.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTagIsRejectedBeforeTheDomainSeesIt(string tag)
    {
        var list = GivenTheCallerOwnsAList();
        IReadOnlyList<string> tags = ["urgent", tag];

        var result = await UseCase().ExecuteAsync(
            new AddTodoItemCommand(list.Id, "Buy milk", null, tags),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATagLongerThanTheDomainAllows_IsRejected()
    {
        var list = GivenTheCallerOwnsAList();
        IReadOnlyList<string> tags = [new string('a', Tag.MaxLength + 1)];

        var result = await UseCase().ExecuteAsync(
            new AddTodoItemCommand(list.Id, "Buy milk", null, tags),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnInvalidCommand_DoesNotCommit()
    {
        GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(ACommandFor(Guid.Empty, ""), TestToken);

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

        var result = await UseCase().ExecuteAsync(ACommandFor(missingId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ownership check. Deleting the <c>OwnerId</c> comparison lets any authenticated
    /// caller write into any list in the database, and turns this red.
    /// </summary>
    [Fact]
    public async Task AnotherUsersList_DoesNotReceiveTheItem()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(ACommandFor(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        foreign.Items.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherUsersList_IsIndistinguishableFromAMissingOne()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var foreignResult = await UseCase().ExecuteAsync(ACommandFor(foreign.Id), TestToken);
        var missingResult = await UseCase().ExecuteAsync(ACommandFor(missingId), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    #endregion

    #region Invariants surfaced as conflicts

    /// <summary>
    /// A duplicate title depends on the current contents of the aggregate, so the caller
    /// could not have avoided it by sending better input: it is a conflict, not a
    /// validation error, and it must never escape as an exception.
    /// </summary>
    [Fact]
    public async Task ADuplicateTitle_BecomesAConflict()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _, "Buy milk");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(ACommandFor(list.Id, "BUY MILK"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("domain.invariantViolated");
        list.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ADuplicateTitle_DoesNotCommit()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _, "Buy milk");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        await UseCase().ExecuteAsync(ACommandFor(list.Id, "Buy milk"), TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFullList_BecomesAConflict()
    {
        var list = ATodoList.OwnedByAndFull(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(ACommandFor(list.Id, "one too many"), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        list.Items.Count.ShouldBe(TodoList.MaxItems);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheConflictMessage_ComesFromTheDomain()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _, "Buy milk");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(ACommandFor(list.Id, "Buy milk"), TestToken);

        result.Error!.Message.ShouldContain("Buy milk");
    }

    #endregion

    #region Success

    [Fact]
    public async Task AValidCommand_AddsTheItemAndReturnsItsId()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(
            new AddTodoItemCommand(list.Id, "Buy milk", "Semi-skimmed", null),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
        var item = list.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(result.Value.Value.Id);
        item.Title.Value.ShouldBe("Buy milk");
        item.Description.ShouldBe("Semi-skimmed");
        result.Value.Value.Title.ShouldBe("Buy milk");
        result.Value.Value.Description.ShouldBe("Semi-skimmed");
        result.Value.Version.ShouldBe(list.Version);
    }

    [Fact]
    public async Task ASuccessfulAdd_CommitsExactlyOnce()
    {
        var list = GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(ACommandFor(list.Id), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One commit covers the item and all its tags. Committing per tag would turn a single
    /// request into several transactions with no rollback path between them.
    /// </summary>
    [Fact]
    public async Task AnAddWithSeveralTags_StillCommitsExactlyOnce()
    {
        var list = GivenTheCallerOwnsAList();
        IReadOnlyList<string> tags = ["urgent", "shopping", "weekly"];

        await UseCase().ExecuteAsync(new AddTodoItemCommand(list.Id, "Buy milk", null, tags), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        list.Items.ShouldHaveSingleItem().Tags.Count.ShouldBe(3);
    }

    [Fact]
    public async Task TheTags_AreNormalisedAndDeduplicatedByTheDomain()
    {
        var list = GivenTheCallerOwnsAList();
        IReadOnlyList<string> tags = ["  URGENT ", "urgent", "Shopping"];

        await UseCase().ExecuteAsync(new AddTodoItemCommand(list.Id, "Buy milk", null, tags), TestToken);

        list.Items.ShouldHaveSingleItem().Tags.Select(tag => tag.Value).ShouldBe(["urgent", "shopping"]);
    }

    [Fact]
    public async Task AnAbsentTagCollection_IsTreatedAsNoTags()
    {
        var list = GivenTheCallerOwnsAList();

        var result = await UseCase().ExecuteAsync(new AddTodoItemCommand(list.Id, "Buy milk", null, null), TestToken);

        result.IsSuccess.ShouldBeTrue();
        list.Items.ShouldHaveSingleItem().Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyTagCollection_IsTreatedAsNoTags()
    {
        var list = GivenTheCallerOwnsAList();
        IReadOnlyList<string> tags = [];

        var result = await UseCase().ExecuteAsync(new AddTodoItemCommand(list.Id, "Buy milk", null, tags), TestToken);

        result.IsSuccess.ShouldBeTrue();
        list.Items.ShouldHaveSingleItem().Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheNewItem_IsOpenAndBelongsToTheList()
    {
        var list = GivenTheCallerOwnsAList();

        await UseCase().ExecuteAsync(ACommandFor(list.Id), TestToken);

        var item = list.Items.ShouldHaveSingleItem();
        item.IsCompleted.ShouldBeFalse();
        item.TodoListId.ShouldBe(list.Id);
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheLoadAndTheCommit()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(ACommandFor(list.Id), cancellation.Token);

        await _repository.Received(1).GetAsync(list.Id, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    #endregion

    private static AddTodoItemCommand ACommandFor(Guid todoListId, string title = "Buy milk") =>
        new(todoListId, title, null, null);

    private AddTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, _validator);

    private AddTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private TodoList GivenTheCallerOwnsAList()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        return list;
    }
}
