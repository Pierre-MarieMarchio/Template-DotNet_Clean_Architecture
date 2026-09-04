using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoItems;

public sealed class GetTodoItemsUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private const uint _listVersion = 55;

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetTodoItemsQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AMissingList_IsReportedAsNotFound()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        var result = await UseCase().ExecuteAsync(new GetTodoItemsQuery(listId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("todoList.notFound");
    }

    /// <summary>The item collection and the single item come from the same query, so they always
    /// carry the same version.</summary>
    [Fact]
    public async Task TheItemsAndTheirVersion_ComeFromTheSameQueryAsTheDetail()
    {
        var listId = Guid.CreateVersion7();
        var items = new[] { new TodoItemDto(Guid.CreateVersion7(), "Buy milk", null, false, null, []) };
        var detail = new Versioned<TodoListDetailDto>(
            new TodoListDetailDto(listId, "Groceries", FixedDateTimeProvider.DefaultInstant, null, items),
            _listVersion);
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await UseCase().ExecuteAsync(new GetTodoItemsQuery(listId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeSameAs(items);
        result.Value.Version.ShouldBe(_listVersion);
    }

    private GetTodoItemsUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser, new GetTodoItemsQueryValidator());

    private GetTodoItemsUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
