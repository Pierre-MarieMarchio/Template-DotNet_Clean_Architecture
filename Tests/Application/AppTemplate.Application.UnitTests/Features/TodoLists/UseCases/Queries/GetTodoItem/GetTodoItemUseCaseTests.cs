using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoItem;

public sealed class GetTodoItemUseCaseTests
{
    private const uint _listVersion = 8080;

    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

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

        await _queries.DidNotReceive().GetDetailAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

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
        var otherCallerId = UserId.Create(Guid.CreateVersion7());
        var listId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId))
            .ExecuteAsync(new GetTodoItemQuery(listId, itemId), TestToken);

        await _queries.Received(1).GetDetailAsync(listId, otherCallerId, Arg.Any<CancellationToken>());
        await _queries.DidNotReceive().GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>());
    }

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

    private static TodoItemDto AnItem(Guid itemId, string title = "Milk") =>
        new(itemId, title, null, false, null, []);

    private GetTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser, new GetTodoItemQueryValidator());

    private GetTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private void GivenTheListHolds(Guid listId, params TodoItemDto[] items) =>
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns(new Versioned<TodoListDetailDto>(
                new TodoListDetailDto(listId, "Groceries", StubDateTimeProvider.DefaultInstant, null, items),
                _listVersion));
}
