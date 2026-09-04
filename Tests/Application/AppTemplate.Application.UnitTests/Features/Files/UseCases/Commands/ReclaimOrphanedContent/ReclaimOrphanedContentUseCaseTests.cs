using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Domain.Features.Files.ValueObjects;
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

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Same trap as the abandonment purge: the worker's current user throws rather than invent a
    /// caller, and the objects swept here belong to every owner and to none of them.
    /// </summary>
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

    /// <summary>
    /// The whole guarantee, in one sentence: an object no row names is rubbish, and it is deleted —
    /// whether or not any deletion event was ever delivered.
    /// </summary>
    [Fact]
    public async Task AnObjectNoRowNames_IsDeleted()
    {
        GivenTheStoreListsOnePageOf(_liveKey, _orphanKey);
        GivenTheseKeysAreStillNamedByARow(_liveKey);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        await _content.Received(1).DeleteAsync(_orphanKey, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The other half, and the one that would be catastrophic to get wrong: an object a row still
    /// names is left exactly where it is. Inverting the <c>live.Contains</c> test in the use case
    /// deletes every live file in the bucket and turns this red.
    /// </summary>
    [Fact]
    public async Task AnObjectARowStillNames_IsLeftAlone()
    {
        GivenTheStoreListsOnePageOf(_liveKey, _orphanKey);
        GivenTheseKeysAreStillNamedByARow(_liveKey);

        await UseCase().ExecuteAsync(TestToken);

        await _content.DidNotReceive().DeleteAsync(_liveKey, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ordering that keeps a file registered mid-pass safe. The page is listed first and the
    /// rows are asked about it second; reading the live keys first would let a file registered in
    /// between be listed as an object with no row on record, and the pass would delete the bytes of
    /// a file its owner had just uploaded.
    /// </summary>
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

    /// <summary>
    /// The rows are asked about the page the store just reported, not about the whole table: the
    /// question is bounded by what the caller already holds, whatever the size of the bucket.
    /// </summary>
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
        // Every argument of the same type needs a matcher of its own: mixing a raw value with
        // Arg.Any for two strings is ambiguous, and NSubstitute says so rather than guessing.
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

    /// <summary>
    /// The walk starts at the reserved partition prefix, which is the first segment of every key the
    /// domain mints. Starting anywhere else would leave part of the store unswept for ever.
    /// </summary>
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

    /// <summary>
    /// A store whose listing never ends must not hang the pass. The valve is what makes the walk
    /// terminate; the next pass restarts from the beginning, where every orphan already reached is
    /// gone.
    /// </summary>
    [Fact]
    public async Task AnEndlessListing_StopsAtTheSafetyValve()
    {
        _inventory.ListKeysAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Keyset<string>([], 0, "always-more"));

        // Reaching this assertion at all is the assertion: without the valve the walk never returns.
        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
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
        NullLogger<ReclaimOrphanedContentUseCase>.Instance);
}
