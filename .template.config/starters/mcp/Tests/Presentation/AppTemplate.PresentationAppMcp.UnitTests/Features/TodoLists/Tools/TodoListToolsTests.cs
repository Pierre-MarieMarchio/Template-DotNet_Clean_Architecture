using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using AppTemplate.PresentationApp.Features.TodoLists.Tools;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Features.TodoLists.Tools;

public sealed class TodoListToolsTests
{
    private static readonly TodoListDetailDto _list = new(Guid.CreateVersion7(), "Errands", DateTimeOffset.UnixEpoch, null, []);

    private static readonly string[] _tags = ["shop"];

    private static readonly TodoItemDto _item = new(Guid.CreateVersion7(), "Buy milk", null, false, null, _tags);

    private readonly IGetTodoListsUseCase _getTodoLists = Substitute.For<IGetTodoListsUseCase>();

    private readonly IGetTodoListUseCase _getTodoList = Substitute.For<IGetTodoListUseCase>();

    private readonly ICreateTodoListUseCase _createTodoList = Substitute.For<ICreateTodoListUseCase>();

    private readonly IAddTodoItemUseCase _addTodoItem = Substitute.For<IAddTodoItemUseCase>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private TodoListTools Tools => new(_getTodoLists, _getTodoList, _createTodoList, _addTodoItem);

    [Fact]
    public async Task ListTodoLists_AsksForThePageItWasGiven()
    {
        _getTodoLists.ExecuteAsync(Arg.Any<GetTodoListsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PagedResult.Offset<TodoListSummaryDto>([], 1, 10, 0)));

        var answer = await Tools.ListTodoListsAsync(1, 10, Cancellation);

        answer.StructuredContent!.Value.GetProperty("pageSize").GetInt32().ShouldBe(10);
        await _getTodoLists.Received(1).ExecuteAsync(GetTodoListsQuery.Offset(1, 10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTodoList_AnswersTheListItWasAskedFor()
    {
        _getTodoList.ExecuteAsync(new GetTodoListQuery(_list.Id), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Versioned<TodoListDetailDto>(_list, 2)));

        var answer = await Tools.GetTodoListAsync(_list.Id, Cancellation);

        answer.StructuredContent!.Value.GetProperty("name").GetString().ShouldBe("Errands");
    }

    [Fact]
    public async Task CreateTodoList_CreatesTheListItWasGiven()
    {
        _createTodoList.ExecuteAsync(new CreateTodoListCommand("Errands"), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Versioned<TodoListDetailDto>(_list, 1)));

        var answer = await Tools.CreateTodoListAsync("Errands", Cancellation);

        answer.StructuredContent!.Value.GetProperty("id").GetGuid().ShouldBe(_list.Id);
    }

    [Fact]
    public async Task AddTodoItem_AddsTheItemItWasGiven_WithoutAPrecondition()
    {
        _addTodoItem.ExecuteAsync(Arg.Any<AddTodoItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Versioned<TodoItemDto>(_item, 2)));

        var answer = await Tools.AddTodoItemAsync(_list.Id, "Buy milk", null, _tags, Cancellation);

        answer.StructuredContent!.Value.GetProperty("title").GetString().ShouldBe("Buy milk");
        await _addTodoItem.Received(1).ExecuteAsync(
            Arg.Is<AddTodoItemCommand>(command => command.TodoListId == _list.Id
                && command.Title == "Buy milk"
                && command.Tags!.SequenceEqual(_tags)
                && command.Precondition == null),
            Arg.Any<CancellationToken>());
    }
}
