using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoList;

public sealed class GetTodoListUseCaseTests
{
    private const uint _listVersion = 9090;

    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_QueriesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetTodoListQuery(Guid.CreateVersion7()), TestToken);

        await _queries.DidNotReceive().GetDetailAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

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
        var otherCallerId = UserId.Create(Guid.CreateVersion7());
        var listId = Guid.CreateVersion7();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId)).ExecuteAsync(new GetTodoListQuery(listId), TestToken);

        await _queries.Received(1).GetDetailAsync(listId, otherCallerId, Arg.Any<CancellationToken>());
        await _queries.DidNotReceive().GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>());
    }

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

    private static Versioned<TodoListDetailDto> ADetailFor(Guid listId) =>
        new(new TodoListDetailDto(listId, "Groceries", StubDateTimeProvider.DefaultInstant, null, []), _listVersion);

    private GetTodoListUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser, new GetTodoListQueryValidator());

    private GetTodoListUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
