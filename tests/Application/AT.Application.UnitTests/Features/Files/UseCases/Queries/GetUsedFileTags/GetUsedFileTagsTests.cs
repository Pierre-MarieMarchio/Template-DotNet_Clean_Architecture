using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.GetUsedFileTags;

public sealed class GetUsedFileTagsTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly IStoredFileTagQueries _queries = Substitute.For<IStoredFileTagQueries>();
    private readonly RecordingCacheStore _cache = new();

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var useCase = new GetUsedFileTagsQueryHandler(_queries, _cache, StubCurrentUser.Anonymous);

        var result = await useCase.Handle(new GetUsedFileTagsQuery(), TestToken);

        result.IsFailure.ShouldBeTrue();
        await _queries.DidNotReceiveWithAnyArgs().GetUsedTagsForOwnerAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ItAnswersTheTagsTheOwnerHasUsed()
    {
        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(["invoice", "scan"]);

        var result = await UseCase().Handle(new GetUsedFileTagsQuery(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(["invoice", "scan"]);
    }

    [Fact]
    public async Task ASecondCall_IsServedFromTheCache()
    {
        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(["invoice"]);

        await UseCase().Handle(new GetUsedFileTagsQuery(), TestToken);
        var second = await UseCase().Handle(new GetUsedFileTagsQuery(), TestToken);

        second.Value.ShouldBe(["invoice"]);
        _cache.Misses.ShouldBe(1);
        await _queries.Received(1).GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoOwners_DoNotShareAnEntry()
    {
        var otherId = UserId.Create(Guid.CreateVersion7());

        _queries.GetUsedTagsForOwnerAsync(_callerId, Arg.Any<CancellationToken>()).Returns(["mine"]);
        _queries.GetUsedTagsForOwnerAsync(otherId, Arg.Any<CancellationToken>()).Returns(["theirs"]);

        var mine = await UseCase().Handle(new GetUsedFileTagsQuery(), TestToken);
        var theirs = await UseCaseFor(otherId).Handle(new GetUsedFileTagsQuery(), TestToken);

        mine.Value.ShouldBe(["mine"]);
        theirs.Value.ShouldBe(["theirs"]);
    }

    [Fact]
    public void TheFileScope_IsNotTheTodoItemScope() =>
        UsedTagsCache.KeyFor(UsedTagsCache.FileScope, _callerId)
            .ShouldNotBe(UsedTagsCache.KeyFor(UsedTagsCache.TodoItemScope, _callerId));

    private GetUsedFileTagsQueryHandler UseCase() => UseCaseFor(_callerId);

    private GetUsedFileTagsQueryHandler UseCaseFor(UserId ownerId) =>
        new(_queries, _cache, StubCurrentUser.WithId(ownerId));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
}
