using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoItems;

public sealed class GetTodoItemsTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());
    private const uint _listVersion = 55;

    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .Handle(new GetTodoItemsQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AMissingList_IsReportedAsNotFound()
    {
        var listId = Guid.CreateVersion7();
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<TodoListDetailDto>?)null);

        var result = await UseCase().Handle(new GetTodoItemsQuery(listId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("todoList.notFound");
    }

    [Fact]
    public async Task TheItemsAndTheirVersion_ComeFromTheSameQueryAsTheDetail()
    {
        var listId = Guid.CreateVersion7();
        var items = new[] { new TodoItemDto(Guid.CreateVersion7(), "Buy milk", null, false, null, []) };
        var detail = new Versioned<TodoListDetailDto>(
            new TodoListDetailDto(listId, "Groceries", StubDateTimeProvider.DefaultInstant, null, items),
            _listVersion);
        _queries.GetDetailAsync(listId, _callerId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await UseCase().Handle(new GetTodoItemsQuery(listId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeSameAs(items);
        result.Value.Version.ShouldBe(_listVersion);
    }

    private ValidatedHandler<GetTodoItemsQuery, Result<Versioned<IReadOnlyList<TodoItemDto>>>> UseCaseFor(ICurrentUser currentUser) =>
        new(new GetTodoItemsQueryHandler(_queries, currentUser).Handle, new GetTodoItemsQueryValidator());

    private ValidatedHandler<GetTodoItemsQuery, Result<Versioned<IReadOnlyList<TodoItemDto>>>> UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
