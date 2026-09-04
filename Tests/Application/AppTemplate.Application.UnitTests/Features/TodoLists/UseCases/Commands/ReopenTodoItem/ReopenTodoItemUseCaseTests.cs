using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.ReopenTodoItem;

public sealed class ReopenTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new ReopenTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFound()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new ReopenTodoItemCommand(list.Id, Guid.CreateVersion7()),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("todoItem.notFound");
    }

    [Fact]
    public async Task ACompletedItem_IsReopenedAndRaisesTheReopenedEvent()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        list.CompleteItem(itemId, FixedDateTimeProvider.DefaultInstant);
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new ReopenTodoItemCommand(list.Id, itemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.IsCompleted.ShouldBeFalse();
        list.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TodoItemReopenedDomainEvent>();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Idempotent: reopening an already-open item succeeds without raising an event.</summary>
    [Fact]
    public async Task AnAlreadyOpenItem_IsAcceptedAsANoOp()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new ReopenTodoItemCommand(list.Id, itemId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        list.DomainEvents.ShouldBeEmpty();
    }

    private ReopenTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, _clock, new ReopenTodoItemCommandValidator());

    private ReopenTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
