using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using AppTemplate.Infrastructure.Persistence.Features.Files.Tracking;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

/// <summary>
/// The three jobs EF's change tracker cannot do for aggregates it does not track: the identity map, the
/// note that a row is on its way out, and the drain that domain-event dispatch depends on.
/// </summary>
/// <remarks>
/// No database and no <c>DbContext</c> here. <c>FlushTo</c> needs EF's change tracker and is covered end
/// to end in the integration suite; everything below is the tracker's own bookkeeping.
/// <para>
/// The drain matters more for this feature than for most: <c>StoredFileDeletedDomainEvent</c> is raised
/// on the way out, and it is what reclaims a deleted file's bytes promptly rather than at the next
/// sweep. An event lost between <c>Remove</c> and the commit costs storage until then.
/// </para>
/// </remarks>
public sealed class StoredFileTrackerTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
    private static readonly DateTimeOffset _confirmedAt = _registeredAt.AddMinutes(3);

    private readonly StoredFileMapper _mapper = new();

    // ---- The identity map ----------------------------------------------------------------------

    [Fact]
    public void Find_ReturnsTheVerySameInstanceEveryTime()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();

        Track(tracker, aggregate);

        var first = tracker.Find(aggregate.Id);
        var second = tracker.Find(aggregate.Id);

        first.ShouldBeSameAs(aggregate);
        second.ShouldBeSameAs(
            aggregate,
            "two callers in one request holding different copies would each decide against their own, "
            + "and the flush would keep whichever it saw last.");
    }

    [Fact]
    public void Find_ReturnsNothing_ForAnAggregateNobodyLoaded()
    {
        ATracker().Find(Guid.CreateVersion7()).ShouldBeNull();
    }

    [Fact]
    public void Find_StopsReturningARemovedAggregate_ButItsRowIsStillThere()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();
        var record = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, record);

        tracker.Find(aggregate.Id).ShouldBeNull("a deleted aggregate must not be handed out again");
        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(
            record,
            "the row is still staged for deletion, and the repository needs it to attach the token.");
    }

    // ---- Draining ------------------------------------------------------------------------------

    [Fact]
    public void DrainDomainEvents_YieldsEachEventExactlyOnce()
    {
        var tracker = ATracker();
        var aggregate = AConfirmedFile();
        Track(tracker, aggregate);

        var drained = tracker.DrainDomainEvents();

        drained.ShouldHaveSingleItem().ShouldBeOfType<StoredFileMadeAvailableDomainEvent>();
        tracker.DrainDomainEvents().ShouldBeEmpty(
            "an event that was taken cannot be taken again, or a second save in the same request would "
            + "publish everything the first one did.");
    }

    [Fact]
    public void DrainDomainEvents_CollectsFromEveryTrackedAggregate()
    {
        var tracker = ATracker();
        var first = AConfirmedFile();
        var second = AConfirmedFile();

        Track(tracker, first);
        Track(tracker, second);

        tracker.DrainDomainEvents()
            .OfType<StoredFileMadeAvailableDomainEvent>()
            .Select(raised => raised.StoredFileId)
            .ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    /// <summary>
    /// The deletion path, and the one that costs storage when it fails. <c>Delete</c> raises the event
    /// that reclaims the bytes now; the row is staged for removal in the same breath, so a tracker that
    /// stopped draining a removed aggregate would leave every deleted file's content on the store until
    /// the next sweep.
    /// </summary>
    [Fact]
    public void DrainDomainEvents_StillYieldsTheEventsOfARemovedAggregate()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();
        var record = Track(tracker, aggregate);

        aggregate.Delete(_confirmedAt);
        tracker.MarkRemoved(aggregate, record);

        tracker.DrainDomainEvents()
            .ShouldHaveSingleItem()
            .ShouldBeOfType<StoredFileDeletedDomainEvent>();
    }

    /// <summary>
    /// The fallback path in the repository: an aggregate reconstructed elsewhere is in no identity map,
    /// and marking it removed has to take it in. Before it did, the aggregate was never tracked, so its
    /// events were never drained and never published — silently.
    /// </summary>
    [Fact]
    public void MarkRemoved_TakesInAnAggregateThatWasNeverTracked()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();
        aggregate.Delete(_confirmedAt);

        tracker.MarkRemoved(aggregate, _mapper.ToNewRecord(aggregate));

        tracker.DrainDomainEvents().ShouldHaveSingleItem();
    }

    /// <summary>
    /// The ordinary path, where the delete follows a load. The row already in the identity map is the one
    /// EF is tracking; a removal must not swap it for a stub, or the flush would write onto an object the
    /// change tracker has never seen.
    /// </summary>
    [Fact]
    public void MarkRemoved_KeepsTheTrackedRow_WhenTheAggregateIsAlreadyKnown()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();
        var tracked = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, new StoredFileRecord { Id = aggregate.Id });

        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(tracked);
    }

    // ---- Restoring after a failed save ---------------------------------------------------------

    [Fact]
    public void Restore_HandsTheEventsBackOnTheNextDrain()
    {
        var tracker = ATracker();
        var aggregate = AConfirmedFile();
        Track(tracker, aggregate);

        var drained = tracker.DrainDomainEvents();
        tracker.Restore(drained);

        tracker.DrainDomainEvents().ShouldBe(drained);
        tracker.DrainDomainEvents().ShouldBeEmpty("restored events are drained once, like any other");
    }

    [Fact]
    public void Restore_PutsTheOlderEventsAheadOfAnythingRaisedSince()
    {
        var tracker = ATracker();
        var first = AConfirmedFile();
        Track(tracker, first);

        var drained = tracker.DrainDomainEvents();
        drained.ShouldHaveSingleItem();

        var second = AConfirmedFile();
        Track(tracker, second);
        tracker.Restore(drained);

        tracker.DrainDomainEvents()
            .OfType<StoredFileMadeAvailableDomainEvent>()
            .Select(raised => raised.StoredFileId)
            .ShouldBe([first.Id, second.Id]);
    }

    [Fact]
    public void Restore_RejectsNull()
    {
        Should.Throw<ArgumentNullException>(() => ATracker().Restore(null!));
    }

    // ---- Fixture -------------------------------------------------------------------------------

    private StoredFileTracker ATracker() => new(_mapper);

    /// <summary>
    /// A freshly registered file, through the real factory — so its key is minted the way a real one is
    /// and each sample gets its own id.
    /// </summary>
    private static StoredFile ARegisteredFile() => StoredFile.Register(
        AStoredFileAggregate.OwnerId,
        StoredFileName.Create(AStoredFileAggregate.NameValue),
        DeclaredMediaType.Create(AStoredFileAggregate.DeclaredMediaTypeValue),
        FileSize.Create(AStoredFileAggregate.SizeInBytes),
        Sha256Checksum.Create(AStoredFileAggregate.ChecksumValue),
        _registeredAt);

    /// <summary>A file that has raised its confirmation event, ready to be tracked.</summary>
    private static StoredFile AConfirmedFile()
    {
        var storedFile = ARegisteredFile();

        storedFile.ConfirmDeposit(
            FileSize.Create(AStoredFileAggregate.SizeInBytes),
            Sha256Checksum.Create(AStoredFileAggregate.ChecksumValue));
        storedFile.MakeAvailable(_confirmedAt);

        return storedFile;
    }

    /// <summary>Registers an aggregate the way the repository does, and hands back its row.</summary>
    private StoredFileRecord Track(StoredFileTracker tracker, StoredFile aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);
        tracker.Track(aggregate, record);

        return record;
    }
}
