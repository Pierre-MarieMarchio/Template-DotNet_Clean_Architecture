using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;

public sealed class ReplaceTodoItemTagsUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(
            new ReplaceTodoItemTagsCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), ["urgent"]),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task ANullTagSet_IsRejected()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new ReplaceTodoItemTagsCommand(list.Id, itemId, null!),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AnUnknownItemId_IsReportedAsNotFound()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out _);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new ReplaceTodoItemTagsCommand(list.Id, Guid.CreateVersion7(), ["urgent"]),
            TestToken);

        result.Error!.Code.ShouldBe("todoItem.notFound");
    }

    [Fact]
    public async Task TheTagSet_IsReplacedWholesale()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        list.AddTagToItem(itemId, "urgent");
        list.AddTagToItem(itemId, "shopping");
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new ReplaceTodoItemTagsCommand(list.Id, itemId, ["weekly"]),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Tags.ShouldBe(["weekly"]);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyTagSet_ClearsTheTags()
    {
        var list = ATodoList.OwnedByWithItem(_callerId, out var itemId);
        list.AddTagToItem(itemId, "urgent");
        list.ClearDomainEvents();
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await UseCase().ExecuteAsync(
            new ReplaceTodoItemTagsCommand(list.Id, itemId, []),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Tags.ShouldBeEmpty();
    }

    private ReplaceTodoItemTagsUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(new TodoListAccess(_repository, currentUser), _unitOfWork, new ReplaceTodoItemTagsCommandValidator());

    private ReplaceTodoItemTagsUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
