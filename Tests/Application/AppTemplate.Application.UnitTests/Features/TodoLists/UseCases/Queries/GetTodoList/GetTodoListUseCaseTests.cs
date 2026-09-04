using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoList;

public sealed class GetTodoListUseCaseTests
{
    private const uint _listVersion = 9090;

    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    /// <summary>
    /// An anonymous caller must not reach the read side at all: a query issued without an owner is
    /// the shape of query that returns every user's rows.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_QueriesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListQuery(Guid.CreateVersion7()), TestToken);

        await _queries.DidNotReceive().GetDetailAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Ownership

    /// <summary>
    /// Ownership is part of the query, not a check after the fact: a read that fetches first
    /// and compares later has already loaded another user's data into memory. Dropping the
    /// owner argument turns this red.
    /// </summary>
    [Fact]
    public async Task TheQuery_IsAlwaysScopedToTheCallersId()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>()).Returns(ADetailFor(listId));

        await UseCase().ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        await _queries.Received(1).GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOwnerScope_FollowsTheCallerAndNotTheRequest()
    {
        var otherCallerId = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId)).ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        await _queries.Received(1).GetDetailAsync(listId, otherCallerId, Arg.Any<CancellationToken>());
        await _queries.DidNotReceive().GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The port answers <c>null</c> both for "no such list" and for "not yours", and the use
    /// case turns that into one not-found result — so the two cannot be told apart.
    /// </summary>
    [Fact]
    public async Task AListTheQueryDoesNotReturn_IsReportedAsNotFoundRatherThanThrown()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        var result = await UseCase().ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
    }

    [Fact]
    public async Task ANotFoundResult_MentionsTheRequestedId()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        var result = await UseCase().ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        result.Error!.Message.ShouldContain(listId.ToString());
    }

    #endregion

    #region Success

    [Fact]
    public async Task AListTheQueryReturns_IsHandedBackUnchanged()
    {
        var listId = Guid.CreateVersion7();
        var detail = ADetailFor(listId);
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await UseCase().ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(detail);
    }

    /// <summary>
    /// The version comes back beside the representation and not inside it. The DTO is the client's
    /// contract, and a version field in it would be a storage concept a caller could start reasoning
    /// about; the transport publishes it as a validator instead.
    /// </summary>
    [Fact]
    public async Task TheVersion_TravelsBesideTheDtoRatherThanInsideIt()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>()).Returns(ADetailFor(listId));

        var result = await UseCase().ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        result.Value.Version.ShouldBe(_listVersion);
        typeof(TodoListDetailDto).GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain("Version");
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheQuery()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>()).Returns(ADetailFor(listId));
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new GetTodoListQuery(listId), cancellation.Token);

        await _queries.Received(1).GetDetailAsync(listId, _callerId, cancellation.Token);
    }

    #endregion

    private static Versioned<TodoListDetailDto> ADetailFor(Guid listId) =>
        new(new TodoListDetailDto(listId, "Groceries", StubDateTimeProvider.DefaultInstant, null, []), _listVersion);

    private GetTodoListUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser, new GetTodoListQueryValidator());

    private GetTodoListUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
