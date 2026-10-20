using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Tracking;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

public sealed class StoredFileTrackerTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
    private static readonly DateTimeOffset _confirmedAt = _registeredAt.AddMinutes(3);

    private readonly StoredFileMapper _mapper = new();

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
    public void Find_ReturnsNothing_ForAnAggregateNobodyLoaded() =>
        ATracker().Find(Guid.CreateVersion7()).ShouldBeNull();

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

    [Fact]
    public void MarkRemoved_TakesInAnAggregateThatWasNeverTracked()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();
        aggregate.Delete(_confirmedAt);

        tracker.MarkRemoved(aggregate, _mapper.ToNewRecord(aggregate));

        tracker.DrainDomainEvents().ShouldHaveSingleItem();
    }

    [Fact]
    public void MarkRemoved_KeepsTheTrackedRow_WhenTheAggregateIsAlreadyKnown()
    {
        var tracker = ATracker();
        var aggregate = ARegisteredFile();
        var tracked = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, new StoredFileRecord { Id = aggregate.Id });

        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(tracked);
    }

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
    public void Restore_RejectsNull() => Should.Throw<ArgumentNullException>(() => ATracker().Restore(null!));

    private StoredFileTracker ATracker() => new(_mapper);

    private static StoredFile ARegisteredFile() => StoredFile.Register(
        AStoredFileAggregate.OwnerId,
        FileCategory.Create(AStoredFileAggregate.CategoryName),
        StoredFileName.Create(AStoredFileAggregate.NameValue),
        DeclaredMediaType.Create(AStoredFileAggregate.DeclaredMediaTypeValue),
        FileSize.Create(AStoredFileAggregate.SizeInBytes),
        Sha256Checksum.Create(AStoredFileAggregate.ChecksumValue),
        _registeredAt);

    private static StoredFile AConfirmedFile()
    {
        var storedFile = ARegisteredFile();

        storedFile.ConfirmDeposit(
            FileSize.Create(AStoredFileAggregate.SizeInBytes),
            Sha256Checksum.Create(AStoredFileAggregate.ChecksumValue));
        storedFile.MakeAvailable(_confirmedAt);

        return storedFile;
    }

    private StoredFileRecord Track(StoredFileTracker tracker, StoredFile aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);
        tracker.Track(aggregate, record);

        return record;
    }
}
