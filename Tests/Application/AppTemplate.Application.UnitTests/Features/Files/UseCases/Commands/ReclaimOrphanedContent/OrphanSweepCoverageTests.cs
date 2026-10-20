using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

public sealed class OrphanSweepCoverageTests
{
    private const int _pageSize = 2;
    private const int _maxPagesPerPass = 2;

    private static readonly string[] _live =
    [
        "t0/202608/a1", "t0/202608/a2", "t0/202608/a3", "t0/202608/a4",
    ];

    private static readonly string[] _orphans =
    [
        "t0/202608/b1", "t0/202608/b2", "t0/202608/b3",
    ];

    private readonly InMemoryBucket _bucket = new();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly RecordingOrphanSweepCheckpoint _checkpoint = new();

    public OrphanSweepCoverageTests() =>
        _queries.GetLiveObjectKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<string>)[.. call.Arg<IReadOnlyList<string>>().Intersect(_live)]);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OrphansBeyondOnePassesBudget_AreReclaimedByLaterPasses()
    {
        _bucket.Put([.. _live, .. _orphans]);

        for (int pass = 0; pass < 5; pass++)
        {
            await UseCase().ExecuteAsync(TestToken);
        }

        _bucket.Keys.ShouldBe(_live, ignoreOrder: true);
    }

    [Fact]
    public async Task EveryObject_IsVisitedWithinTheBoundAcrossRestarts()
    {
        _bucket.Put([.. _live, .. _orphans]);

        await UseCase().ExecuteAsync(TestToken);
        _checkpoint.LastKey.ShouldBe("t0/202608/a4");

        var second = await UseCase().ExecuteAsync(TestToken);

        second.Value.ShouldBe(_orphans.Length);
        _bucket.Keys.ShouldBe(_live, ignoreOrder: true);
    }

    [Fact]
    public async Task TheCheckpoint_IsSavedAfterEachPage()
    {
        _bucket.Put([.. _live, .. _orphans]);

        await UseCase().ExecuteAsync(TestToken);

        _checkpoint.Saves.ShouldBe(["t0/202608/a2", "t0/202608/a4"]);
    }

    [Fact]
    public async Task APass_ResumesAfterTheSavedKey()
    {
        _bucket.Put([.. _live, .. _orphans]);
        _checkpoint.LastKey = "t0/202608/a4";

        await UseCase().ExecuteAsync(TestToken);

        _bucket.ListedAfter[0].ShouldBe("t0/202608/a4");
    }

    [Fact]
    public async Task ReachingTheEnd_ResetsTheWalk()
    {
        _bucket.Put([.. _live, .. _orphans]);
        _checkpoint.LastKey = "t0/202608/a4";

        await UseCase().ExecuteAsync(TestToken);

        _checkpoint.LastKey.ShouldBeNull();

        _bucket.Put("t0/202608/a0-orphan");
        await UseCase().ExecuteAsync(TestToken);

        _bucket.ListedAfter[^2].ShouldBeNull();
        _bucket.Keys.ShouldNotContain("t0/202608/a0-orphan");
    }

    [Fact]
    public async Task AFailureMidWalk_ResumesAfterTheLastFinishedPage()
    {
        _bucket.Put([.. _live, .. _orphans]);
        _queries.GetLiveObjectKeysAsync(
                Arg.Is<IReadOnlyList<string>>(keys => keys.Contains("t0/202608/a3")),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new TimeoutException("database unreachable"));

        await Should.ThrowAsync<TimeoutException>(() => UseCase().ExecuteAsync(TestToken));

        _checkpoint.LastKey.ShouldBe("t0/202608/a2");
    }

    private ReclaimOrphanedContentUseCase UseCase() => new(
        _bucket,
        _bucket,
        _queries,
        _checkpoint,
        new StubLeaderLease(),
        NullLogger<ReclaimOrphanedContentUseCase>.Instance,
        _pageSize,
        _maxPagesPerPass);
}
