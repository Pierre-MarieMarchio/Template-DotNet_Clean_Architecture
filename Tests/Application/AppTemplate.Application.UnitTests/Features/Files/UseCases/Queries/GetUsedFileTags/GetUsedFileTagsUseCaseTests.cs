using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.GetUsedFileTags;

/// <summary>
/// The read that goes through the cache: the answer is the owner's, and a second call is served
/// without asking the database again.
/// </summary>
public sealed class GetUsedFileTagsUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly IStoredFileTagQueries _queries = Substitute.For<IStoredFileTagQueries>();
    private readonly RecordingCacheStore _cache = new();

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var useCase = new GetUsedFileTagsUseCase(_queries, _cache, StubCurrentUser.Anonymous);

        var result = await useCase.ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        await _queries.DidNotReceiveWithAnyArgs().GetUsedTagsForOwnerAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ItAnswersTheTagsTheOwnerHasUsed()
    {
        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(["invoice", "scan"]);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(["invoice", "scan"]);
    }

    [Fact]
    public async Task ASecondCall_IsServedFromTheCache()
    {
        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(["invoice"]);

        await UseCase().ExecuteAsync(TestToken);
        var second = await UseCase().ExecuteAsync(TestToken);

        second.Value.ShouldBe(["invoice"]);
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

    /// <summary>
    /// The two features answer this question separately, so their entries must not collide: one
    /// owner's file tags are not their to-do item tags.
    /// </summary>
    [Fact]
    public void TheFileScope_IsNotTheTodoItemScope() =>
        UsedTagsCache.KeyFor(UsedTagsCache.FileScope, _callerId)
            .ShouldNotBe(UsedTagsCache.KeyFor(UsedTagsCache.TodoItemScope, _callerId));

    private GetUsedFileTagsUseCase UseCase() => UseCaseFor(_callerId);

    private GetUsedFileTagsUseCase UseCaseFor(UserId ownerId) =>
        new(_queries, _cache, StubCurrentUser.WithId(ownerId));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
}
