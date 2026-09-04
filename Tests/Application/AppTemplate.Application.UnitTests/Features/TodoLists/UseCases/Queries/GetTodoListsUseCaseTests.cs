using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Collections;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;
using SortDirection = AppTemplate.Application.Common.Collections.SortDirection;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries;

public sealed class GetTodoListsUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(GetTodoListsQuery.Offset(1, 10), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_QueriesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(GetTodoListsQuery.Offset(1, 10), TestToken);

        await _queries.DidNotReceive().GetForOwnerAsync(
            Arg.Any<Guid>(),
            Arg.Any<TodoListPageRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Authentication is checked before anything else, so an anonymous caller cannot tell bad input
    /// from a missing session.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_IsRefusedBeforeAnythingElseIsValidated()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(GetTodoListsQuery.Offset(0, 0), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    #endregion

    #region Ownership

    /// <summary>
    /// The owner comes from <see cref="ICurrentUser"/> and is not part of the query, so no
    /// caller can widen the scope to somebody else's rows.
    /// </summary>
    [Fact]
    public async Task TheQuery_IsAlwaysScopedToTheCallersId()
    {
        GivenThePageIsEmpty();

        await UseCase().ExecuteAsync(GetTodoListsQuery.Offset(1, 10), TestToken);

        await _queries.Received(1).GetForOwnerAsync(
            _callerId,
            Arg.Any<TodoListPageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOwnerScope_FollowsTheCallerAndNotTheRequest()
    {
        var otherCallerId = Guid.CreateVersion7();
        GivenThePageIsEmpty();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId))
            .ExecuteAsync(GetTodoListsQuery.Offset(1, 10), TestToken);

        await _queries.Received(1).GetForOwnerAsync(
            otherCallerId,
            Arg.Any<TodoListPageRequest>(),
            Arg.Any<CancellationToken>());
        await _queries.DidNotReceive().GetForOwnerAsync(
            _callerId,
            Arg.Any<TodoListPageRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// There is no overload, no optional owner and no <c>Guid.Empty</c> sentinel that could
    /// stand for "everybody": every read-side method takes an owner.
    /// </summary>
    [Fact]
    public void EveryMethodOnTheReadSidePort_TakesAnOwner() =>
        typeof(ITodoListQueries).GetMethods()
            .ShouldAllBe(method => method.GetParameters().Any(parameter => parameter.Name == "ownerId"));

    #endregion

    /// <summary>
    /// All of paging/sort/cursor/filter parsing lives in <c>TodoListRequestBinder</c> now, and is
    /// exercised exhaustively in <c>TodoListRequestBinderTests</c>. This use case only has to prove
    /// it delegates to the binder and propagates its failure — one representative case.
    /// </summary>
    [Fact]
    public async Task ABinderFailure_IsPropagatedWithoutReachingThePort()
    {
        var result = await UseCase().ExecuteAsync(GetTodoListsQuery.Offset(0, 10), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");

        await _queries.DidNotReceive().GetForOwnerAsync(
            Arg.Any<Guid>(),
            Arg.Any<TodoListPageRequest>(),
            Arg.Any<CancellationToken>());
    }

    #region Success

    [Fact]
    public async Task TheRequestedPage_IsHandedBackUnchanged()
    {
        var page = PagedResult.Offset<TodoListSummaryDto>([ASummary()], 2, 10, 42);
        _queries.GetForOwnerAsync(_callerId, Arg.Any<TodoListPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await UseCase().ExecuteAsync(GetTodoListsQuery.Offset(2, 10), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(page);
    }

    [Fact]
    public async Task ThePortReceivesTheValidatedRequest()
    {
        GivenThePageIsEmpty();

        await UseCase().ExecuteAsync(
            new GetTodoListsQuery(null, 3, 25, null, "name:asc", "milk", null, null),
            TestToken);

        await _queries.Received(1).GetForOwnerAsync(
            _callerId,
            Arg.Is<TodoListPageRequest>(request =>
                request != null
                && request.Paging.Mode == PagingMode.Offset
                && request.Paging.Page == 3
                && request.Paging.PageSize == 25
                && request.Sort.Terms.Count == 1
                && request.Sort.Terms[0].Field == "name"
                && request.Sort.Terms[0].Direction == SortDirection.Ascending
                && request.Filter.Search!.Value == "milk"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheQuery()
    {
        GivenThePageIsEmpty();
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(GetTodoListsQuery.Offset(1, 10), cancellation.Token);

        await _queries.Received(1).GetForOwnerAsync(_callerId, Arg.Any<TodoListPageRequest>(), cancellation.Token);
    }

    #endregion

    private static TodoListSummaryDto ASummary() =>
        new(Guid.CreateVersion7(), "Groceries", 3, 1, FixedDateTimeProvider.DefaultInstant);

    private GetTodoListsUseCase UseCaseFor(ICurrentUser currentUser) => new(_queries, currentUser);

    private GetTodoListsUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private void GivenThePageIsEmpty() =>
        _queries.GetForOwnerAsync(
                Arg.Any<Guid>(),
                Arg.Any<TodoListPageRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<TodoListSummaryDto>([], 1, 10, 0));
}
