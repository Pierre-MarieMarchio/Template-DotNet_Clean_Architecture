using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

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
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListsQuery(1, 10), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_QueriesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListsQuery(1, 10), TestToken);

        await _queries.DidNotReceive().GetForOwnerAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Authentication is checked before the paging bounds, so an anonymous caller cannot
    /// tell a bad page number from a missing session.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_IsRefusedBeforeThePagingIsValidated()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListsQuery(0, 0), TestToken);

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

        await UseCase().ExecuteAsync(new GetTodoListsQuery(1, 10), TestToken);

        await _queries.Received(1).GetForOwnerAsync(_callerId, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOwnerScope_FollowsTheCallerAndNotTheRequest()
    {
        var otherCallerId = Guid.CreateVersion7();
        GivenThePageIsEmpty();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId)).ExecuteAsync(new GetTodoListsQuery(1, 10), TestToken);

        await _queries.Received(1).GetForOwnerAsync(otherCallerId, 1, 10, Arg.Any<CancellationToken>());
        await _queries.DidNotReceive().GetForOwnerAsync(_callerId, 1, 10, Arg.Any<CancellationToken>());
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

    #region Paging bounds

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task APageNumberBelowOne_IsRejected(int page)
    {
        var result = await UseCase().ExecuteAsync(new GetTodoListsQuery(page, 10), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("paging.invalid");
        result.Error.Message.ShouldContain("page number");
    }

    /// <summary>
    /// An unbounded page size is an unbounded query wearing a pagination costume, so both
    /// ends of the range are enforced.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(GetTodoListsUseCase.MaxPageSize + 1)]
    [InlineData(int.MaxValue)]
    public async Task APageSizeOutsideTheAllowedRange_IsRejected(int pageSize)
    {
        var result = await UseCase().ExecuteAsync(new GetTodoListsQuery(1, pageSize), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("paging.invalid");
        result.Error.Message.ShouldContain("page size");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(GetTodoListsUseCase.MaxPageSize)]
    public async Task APageSizeInsideTheAllowedRange_IsAccepted(int pageSize)
    {
        GivenThePageIsEmpty();

        var result = await UseCase().ExecuteAsync(new GetTodoListsQuery(1, pageSize), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Bad paging must be refused before the query runs, otherwise the bound is advisory.
    /// </summary>
    [Fact]
    public async Task InvalidPaging_NeverReachesTheReadSide()
    {
        await UseCase().ExecuteAsync(new GetTodoListsQuery(0, 10), TestToken);
        await UseCase().ExecuteAsync(new GetTodoListsQuery(1, 0), TestToken);
        await UseCase().ExecuteAsync(new GetTodoListsQuery(1, GetTodoListsUseCase.MaxPageSize + 1), TestToken);

        await _queries.DidNotReceive().GetForOwnerAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Asserted against a literal on purpose: every other paging case above derives its input from
    /// <see cref="GetTodoListsUseCase.MaxPageSize"/> and would move with the constant, so raising
    /// the bound out of usefulness would go unnoticed.
    /// </summary>
    [Fact]
    public async Task ThePageSizeCeiling_IsOneHundredRows()
    {
        GetTodoListsUseCase.MaxPageSize.ShouldBe(100);
        GivenThePageIsEmpty();

        (await UseCase().ExecuteAsync(new GetTodoListsQuery(1, 100), TestToken)).IsSuccess.ShouldBeTrue();

        var beyond = await UseCase().ExecuteAsync(new GetTodoListsQuery(1, 101), TestToken);

        beyond.IsFailure.ShouldBeTrue();
        beyond.Error!.Code.ShouldBe("paging.invalid");
    }

    /// <summary>
    /// The page number is checked before the page size, so a request that is wrong in both
    /// ways is told about the page first — a stable answer, not an arbitrary one.
    /// </summary>
    [Fact]
    public async Task APageNumberIsCheckedBeforeThePageSize()
    {
        var result = await UseCase().ExecuteAsync(new GetTodoListsQuery(0, 0), TestToken);

        result.Error!.Message.ShouldContain("page number");
    }

    #endregion

    #region Success

    [Fact]
    public async Task TheRequestedPage_IsHandedBackUnchanged()
    {
        var page = new PagedResult<TodoListSummaryDto>([ASummary()], 2, 10, 42);
        _queries.GetForOwnerAsync(_callerId, 2, 10, Arg.Any<CancellationToken>()).Returns(page);

        var result = await UseCase().ExecuteAsync(new GetTodoListsQuery(2, 10), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(page);
    }

    [Fact]
    public async Task ThePagingArguments_AreForwardedVerbatim()
    {
        GivenThePageIsEmpty();

        await UseCase().ExecuteAsync(new GetTodoListsQuery(3, 25), TestToken);

        await _queries.Received(1).GetForOwnerAsync(_callerId, 3, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheQuery()
    {
        GivenThePageIsEmpty();
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new GetTodoListsQuery(1, 10), cancellation.Token);

        await _queries.Received(1).GetForOwnerAsync(_callerId, 1, 10, cancellation.Token);
    }

    #endregion

    private static TodoListSummaryDto ASummary() =>
        new(Guid.CreateVersion7(), "Groceries", 3, 1, FixedDateTimeProvider.DefaultInstant);

    private GetTodoListsUseCase UseCaseFor(ICurrentUser currentUser) => new(_queries, currentUser);

    private GetTodoListsUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private void GivenThePageIsEmpty() =>
        _queries.GetForOwnerAsync(
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new PagedResult<TodoListSummaryDto>([], 1, 10, 0));
}
