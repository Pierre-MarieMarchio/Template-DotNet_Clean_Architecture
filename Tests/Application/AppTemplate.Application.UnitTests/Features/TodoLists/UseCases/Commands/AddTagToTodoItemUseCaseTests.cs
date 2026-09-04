using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.Validators;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Stores;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands;

public sealed class AddTagToTodoItemUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(
            new AddTagToTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), "urgent"),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTag_IsRejected(string tag)
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new AddTagToTodoItemCommand(list.Id, itemId, tag), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFound()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new AddTagToTodoItemCommand(list.Id, Guid.CreateVersion7(), "urgent"),
            TestToken);

        result.Error!.Code.ShouldBe("todoItem.notFound");
    }

    [Fact]
    public async Task ATagBeyondTheCap_BecomesAConflict()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);

        for (int i = 0; i < TodoItem.MaxTags; i++)
        {
            list.AddTagToItem(itemId, $"tag-{i}");
        }

        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new AddTagToTodoItemCommand(list.Id, itemId, "one-too-many"),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task AValidTag_IsAddedAndNormalised()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(new AddTagToTodoItemCommand(list.Id, itemId, "  URGENT "), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Tags.ShouldBe(["urgent"]);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private AddTagToTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, new AddTagToTodoItemCommandValidator());

    private AddTagToTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
