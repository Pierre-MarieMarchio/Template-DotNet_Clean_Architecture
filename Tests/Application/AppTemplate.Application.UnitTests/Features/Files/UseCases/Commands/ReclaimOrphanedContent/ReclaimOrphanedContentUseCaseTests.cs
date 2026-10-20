using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

public sealed class ReclaimOrphanedContentUseCaseTests
{
    private const string _liveKey = "t0/202608/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string _orphanKey = "t0/202608/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string _secondOrphanKey = "t0/202608/cccccccccccccccccccccccccccccccc";

    private readonly IFileContentInventory _inventory = Substitute.For<IFileContentInventory>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly RecordingOrphanSweepCheckpoint _checkpoint = new();
    private StubLeaderLease _lease = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void ThePass_NeverReadsTheCurrentUser()
    {
        var parameters = typeof(ReclaimOrphanedContentUseCase)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .ToList();

        parameters.ShouldNotBeEmpty(
            "A use case with no constructor parameters would satisfy the assertion below for free.");

        parameters.ShouldNotContain(parameter => parameter.ParameterType == typeof(ICurrentUser));
    }

    [Fact]
    public async Task AnObjectNoRowNames_IsDeleted()
    {
        GivenTheStoreListsOnePageOf(_liveKey, _orphanKey);
        GivenTheseKeysAreStillNamedByARow(_liveKey);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        await _content.Received(1).DeleteAsync(_orphanKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnObjectARowStillNames_IsLeftAlone()
    {
        GivenTheStoreListsOnePageOf(_liveKey, _orphanKey);
        GivenTheseKeysAreStillNamedByARow(_liveKey);

        await UseCase().ExecuteAsync(TestToken);

        await _content.DidNotReceive().DeleteAsync(_liveKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachPage_IsListedBeforeTheRowsAreAskedAboutIt()
    {
        bool listed = false;
        bool askedBeforeListing = false;

        _inventory.ListKeysAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                listed = true;

                return PagedResult.Keyset<string>([_liveKey, _orphanKey], 2, null);
            });

        _queries.GetLiveObjectKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                askedBeforeListing = !listed;

                return (IReadOnlyList<string>)[_liveKey];
            });

        await UseCase().ExecuteAsync(TestToken);

        askedBeforeListing.ShouldBeFalse();
    }

    [Fact]
    public async Task TheRows_AreAskedAboutExactlyThePageThatWasListed()
    {
        GivenTheStoreListsOnePageOf(_liveKey, _orphanKey);
        GivenTheseKeysAreStillNamedByARow(_liveKey);

        await UseCase().ExecuteAsync(TestToken);

        await _queries.Received(1).GetLiveObjectKeysAsync(
            Arg.Is<IReadOnlyList<string>>(keys => keys != null
                && keys.Count == 2
                && keys.Contains(_liveKey)
                && keys.Contains(_orphanKey)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryPage_IsWalked()
    {
        _inventory.ListKeysAsync(
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == null),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Keyset<string>([_orphanKey], 1, "next"));
        _inventory.ListKeysAsync(
                Arg.Any<string>(),
                Arg.Is<string?>(token => token == "next"),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Keyset<string>([_secondOrphanKey], 1, null));
        GivenTheseKeysAreStillNamedByARow();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(2);
        await _content.Received(1).DeleteAsync(_orphanKey, Arg.Any<CancellationToken>());
        await _content.Received(1).DeleteAsync(_secondOrphanKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyStore_ReclaimsNothingAndAsksNothing()
    {
        GivenTheStoreListsOnePageOf();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _queries.DidNotReceive().GetLiveObjectKeysAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _content.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreWhereEveryObjectIsLive_DeletesNothing()
    {
        GivenTheStoreListsOnePageOf(_liveKey, _orphanKey);
        GivenTheseKeysAreStillNamedByARow(_liveKey, _orphanKey);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _content.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheWalk_StartsAtThePartitionEveryKeyIsMintedUnder()
    {
        GivenTheStoreListsOnePageOf();

        await UseCase().ExecuteAsync(TestToken);

        await _inventory.Received(1).ListKeysAsync(
            Arg.Is<string>(prefix => prefix == $"{ObjectKey.UnpartitionedPrefix}/"),
            Arg.Is<string?>(token => token == null),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEndlessListing_StopsAtTheSafetyValve()
    {
        _inventory.ListKeysAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Keyset<string>([], 0, "always-more"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    [Fact]
    public async Task TheLeaseHeldByAnotherHost_LeavesThePassUndone()
    {
        GivenTheStoreListsOnePageOf(_orphanKey);
        _lease = StubLeaderLease.HeldElsewhere();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _inventory.DidNotReceive().ListKeysAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        _checkpoint.Saves.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheLeaseName_IsPinnedAsALiteral()
    {
        GivenTheStoreListsOnePageOf();

        await UseCase().ExecuteAsync(TestToken);

        _lease.RequestedNames.ShouldBe(["apptemplate.files.reclaim-orphans"]);
    }

    private void GivenTheStoreListsOnePageOf(params string[] keys) =>
        _inventory.ListKeysAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Keyset(keys, keys.Length, null));

    private void GivenTheseKeysAreStillNamedByARow(params string[] keys) =>
        _queries.GetLiveObjectKeysAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)keys);

    private ReclaimOrphanedContentUseCase UseCase() => new(
        _inventory,
        _content,
        _queries,
        _checkpoint,
        _lease,
        NullLogger<ReclaimOrphanedContentUseCase>.Instance);
}
