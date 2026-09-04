using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using AppTemplate.Application.Features.TodoLists.Validators;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries;

public sealed class GetTodoItemUseCaseTests
{
    private const uint _listVersion = 8080;

    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetTodoItemQuery(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_QueriesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetTodoItemQuery(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

        await _queries.DidNotReceive().GetDetailAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ownership

    /// <summary>
    /// An item is reachable only through the list that owns it, so the owner filter that protects
    /// the list protects the item too.
    /// </summary>
    [Fact]
    public async Task TheQuery_IsAlwaysScopedToTheCallersId()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        GivenTheListHolds(listId, AnItem(itemId));

        await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, itemId), TestToken);

        await _queries.Received(1).GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOwnerScope_FollowsTheCallerAndNotTheRequest()
    {
        var otherCallerId = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId))
            .ExecuteAsync(new GetTodoItemQuery(listId, itemId), TestToken);

        await _queries.Received(1).GetDetailAsync(listId, otherCallerId, Arg.Any<CancellationToken>());
        await _queries.DidNotReceive().GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A list the read side does not return is answered as a missing list, never as a missing item:
    /// the latter would confirm that the list exists and belongs to somebody.
    /// </summary>
    [Fact]
    public async Task AnItemOnAListTheQueryDoesNotReturn_IsReportedAsAMissingList()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        var result = await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
    }

    #endregion

    #region Missing item

    [Fact]
    public async Task AnItemTheListDoesNotHold_IsReportedAsNotFound()
    {
        var listId = Guid.CreateVersion7();
        GivenTheListHolds(listId, AnItem(Guid.CreateVersion7()));

        var result = await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoItem.notFound");
    }

    [Fact]
    public async Task AMissingItem_MentionsTheRequestedId()
    {
        var listId = Guid.CreateVersion7();
        var missingItemId = Guid.CreateVersion7();
        GivenTheListHolds(listId, AnItem(Guid.CreateVersion7()));

        var result = await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, missingItemId), TestToken);

        result.Error!.Message.ShouldContain(missingItemId.ToString());
    }

    [Fact]
    public async Task AnEmptyList_ReportsAMissingItemRatherThanThrowing()
    {
        var listId = Guid.CreateVersion7();
        GivenTheListHolds(listId);

        var result = await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, Guid.CreateVersion7()), TestToken);

        result.Error!.Code.ShouldBe("todoItem.notFound");
    }

    #endregion

    #region Success

    [Fact]
    public async Task TheRequestedItem_IsHandedBackUnchanged()
    {
        var listId = Guid.CreateVersion7();
        var wanted = AnItem(Guid.CreateVersion7(), "Milk");
        GivenTheListHolds(listId, AnItem(Guid.CreateVersion7(), "Bread"), wanted);

        var result = await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, wanted.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeSameAs(wanted);
    }

    /// <summary>
    /// The version an item carries is the list's. An item has no version of its own to give: the
    /// root is the consistency boundary, so a change anywhere in the list invalidates what a caller
    /// holding one item was told.
    /// </summary>
    [Fact]
    public async Task TheItem_CarriesTheListsVersion()
    {
        var listId = Guid.CreateVersion7();
        var wanted = AnItem(Guid.CreateVersion7(), "Milk");
        GivenTheListHolds(listId, wanted);

        var result = await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, wanted.Id), TestToken);

        result.Value.Version.ShouldBe(_listVersion);
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheQuery()
    {
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        GivenTheListHolds(listId, AnItem(itemId));
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new GetTodoItemQuery(listId, itemId), cancellation.Token);

        await _queries.Received(1).GetDetailAsync(listId, _callerId, cancellation.Token);
    }

    [Fact]
    public async Task ANullQuery_IsRejected() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

    private static TodoItemDto AnItem(Guid itemId, string title = "Milk") =>
        new(itemId, title, null, false, null, []);

    private GetTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser, new GetTodoItemQueryValidator());

    private GetTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private void GivenTheListHolds(Guid listId, params TodoItemDto[] items) =>
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new Versioned<TodoListDetailDto>(
                new TodoListDetailDto(listId, "Groceries", FixedDateTimeProvider.DefaultInstant, null, items),
                _listVersion));
}
