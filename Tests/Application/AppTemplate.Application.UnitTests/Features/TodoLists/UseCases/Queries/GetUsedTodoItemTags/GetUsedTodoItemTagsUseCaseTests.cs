using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;

/// <summary>
/// The read that goes through the cache: the answer is the owner's, and a second call is served
/// without asking the database again.
/// </summary>
public sealed class GetUsedTodoItemTagsUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly ITodoItemTagQueries _queries = Substitute.For<ITodoItemTagQueries>();
    private readonly RecordingCacheStore _cache = new();

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var useCase = new GetUsedTodoItemTagsUseCase(_queries, _cache, StubCurrentUser.Anonymous);

        var result = await useCase.ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        await _queries.DidNotReceiveWithAnyArgs().GetUsedTagsForOwnerAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ItAnswersTheTagsTheOwnerHasUsed()
    {
        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(["home", "urgent"]);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(["home", "urgent"]);
    }

    [Fact]
    public async Task ASecondCall_IsServedFromTheCache()
    {
        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(["home"]);

        await UseCase().ExecuteAsync(TestToken);
        var second = await UseCase().ExecuteAsync(TestToken);

        second.Value.ShouldBe(["home"]);
        _cache.Misses.ShouldBe(1);
        await _queries.Received(1).GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Two owners are two answers. A key that forgot the owner would serve one caller's tags to
    /// another, which is the failure a cache makes possible and nothing else here would catch.
    /// </summary>
    [Fact]
    public async Task TwoOwners_DoNotShareAnEntry()
    {
        var otherId = UserId.Create(Guid.CreateVersion7());

        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>()).Returns(["mine"]);
        _queries.GetUsedTagsForOwnerAsync(otherId, Arg.Any<CancellationToken>()).Returns(["theirs"]);

        var mine = await UseCase().ExecuteAsync(TestToken);
        var theirs = await UseCaseFor(otherId).ExecuteAsync(TestToken);

        mine.Value.ShouldBe(["mine"]);
        theirs.Value.ShouldBe(["theirs"]);
    }

    private GetUsedTodoItemTagsUseCase UseCase() => UseCaseFor(_callerId);

    private GetUsedTodoItemTagsUseCase UseCaseFor(UserId ownerId) =>
        new(_queries, _cache, StubCurrentUser.WithId(ownerId));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
}
