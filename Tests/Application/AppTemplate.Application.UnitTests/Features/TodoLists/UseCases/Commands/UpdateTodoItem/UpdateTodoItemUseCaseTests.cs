using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.UpdateTodoItem;

public sealed class UpdateTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(
            new UpdateTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "Buy bread", null),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTitle_IsRejected(string title)
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new UpdateTodoItemCommand(list.Id, itemId, title, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFound()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new UpdateTodoItemCommand(list.Id, Guid.CreateVersion7(), "Buy bread", null),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("todoItem.notFound");
    }

    /// <summary>Renaming an item to a title already held by another item is a conflict, not a
    /// validation error: it depends on the aggregate's current contents.</summary>
    [Fact]
    public async Task ATitleAlreadyHeldByAnotherItem_BecomesAConflict()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _, "Buy milk");
        var itemId = list.AddItem("Buy bread", null);
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new UpdateTodoItemCommand(list.Id, itemId, "Buy milk", null),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Renaming an item to its own current title must not collide with itself.</summary>
    [Fact]
    public async Task RenamingAnItemToItsOwnTitle_Succeeds()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId, "Buy milk");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new UpdateTodoItemCommand(list.Id, itemId, "Buy milk", "Updated description"),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Title.ShouldBe("Buy milk");
        result.Value.Value.Description.ShouldBe("Updated description");
    }

    [Fact]
    public async Task AValidUpdate_ReplacesTitleAndDescriptionAndCommitsOnce()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId, "Buy milk");
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new UpdateTodoItemCommand(list.Id, itemId, "Buy bread", "Wholemeal"),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
        list.Items.ShouldHaveSingleItem().Title.Value.ShouldBe("Buy bread");
        result.Value.Version.ShouldBe(list.Version);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private UpdateTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, new UpdateTodoItemCommandValidator());

    private UpdateTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
