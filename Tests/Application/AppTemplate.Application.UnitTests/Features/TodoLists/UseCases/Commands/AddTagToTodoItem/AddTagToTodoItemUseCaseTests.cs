using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;

public sealed class AddTagToTodoItemUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RecordingCacheStore _cache = new();

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

        // Tagging changes the answer the picker reads, so the entry has to go: a suggestion list
        // that never learned about a tag the caller just added is the one staleness a user notices.
        _cache.Removed.ShouldContain(UsedTagsCache.KeyFor(UsedTagsCache.TodoItemScope, _callerId));
    }

    private AddTagToTodoItemUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListService(_repository, currentUser), _unitOfWork, _cache, new AddTagToTodoItemCommandValidator());

    private AddTagToTodoItemUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
