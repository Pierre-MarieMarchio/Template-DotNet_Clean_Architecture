using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;

public sealed class RemoveTagFromTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(
            new RemoveTagFromTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "urgent"),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFound()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new RemoveTagFromTodoItemCommand(list.Id, Guid.CreateVersion7(), "urgent"),
            TestToken);

        result.Error!.Code.ShouldBe("todoItem.notFound");
    }

    /// <summary>Removing a tag the item does not carry is a no-op, never a failure.</summary>
    [Fact]
    public async Task AnAbsentTag_IsAcceptedAsANoOp()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new RemoveTagFromTodoItemCommand(list.Id, itemId, "never-added"),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task APresentTag_IsRemoved()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        list.AddTagToItem(itemId, "urgent");
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new RemoveTagFromTodoItemCommand(list.Id, itemId, "urgent"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Tags.ShouldBeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private RemoveTagFromTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, new RemoveTagFromTodoItemCommandValidator());

    private RemoveTagFromTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
