using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

public sealed class TodoListMapperReconciliationTests
{
    private readonly TodoListMapper _mapper = new();

    [Fact]
    public void WriteTo_LeavesTheAuditColumnsAndTheConcurrencyTokenAlone()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);

        aggregate.Rename("A new name");

        _mapper.WriteTo(aggregate, tracked);

        tracked.Name.ShouldBe("A new name");

        tracked.Version.ShouldBe(_storedVersion, "the token belongs to PostgreSQL, not to the mapper");
        tracked.CreatedAt.ShouldBe(_storedCreatedAt);
        tracked.CreatedBy.ShouldBe(_storedCreatedBy);
        tracked.LastModifiedAt.ShouldBe(_storedLastModifiedAt);
        tracked.LastModifiedBy.ShouldBe(_storedLastModifiedBy);
    }

    [Fact]
    public void WriteTo_MutatesTheTrackedRowsRatherThanReplacingThem()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);
        var itemRowsBefore = tracked.Items.ToList();

        aggregate.Rename("Still the same rows");

        _mapper.WriteTo(aggregate, tracked);

        tracked.Items.ShouldBe(itemRowsBefore, ignoreOrder: true);
    }

    [Fact]
    public void WriteTo_ReportsNoStructuralChange_WhenOnlyTheRootChanged()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);

        aggregate.Rename("Renamed");

        _mapper.WriteTo(aggregate, tracked).ShouldBeFalse(
            "no child row was added or removed, so the caller has no reason to touch the root on their "
            + "behalf — EF's own diff will see the renamed column.");
    }

    [Fact]
    public void WriteTo_AddsANewItemAndReportsIt()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);

        var newItemId = aggregate.AddItem("Post the letter", "Second class");

        _mapper.WriteTo(aggregate, tracked).ShouldBeTrue(
            "a child row was added, so the root has to be marked modified: its concurrency token is the "
            + "arbiter for every write anywhere in the aggregate.");

        var added = tracked.Items.Single(item => item.Id == newItemId);
        added.TodoListId.ShouldBe(aggregate.Id);
        added.Title.ShouldBe("Post the letter");
        added.Description.ShouldBe("Second class");
        added.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void WriteTo_RemovesADeletedItemAndReportsIt()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);
        var doomed = aggregate.Items.First().Id;

        aggregate.RemoveItem(doomed);

        _mapper.WriteTo(aggregate, tracked).ShouldBeTrue();

        tracked.Items.Select(item => item.Id).ShouldNotContain(doomed);
        tracked.Items.Count.ShouldBe(aggregate.Items.Count);
    }

    [Fact]
    public void WriteTo_UpdatesOnlyTheItemThatChanged()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);

        var open = aggregate.Items.Single(item => item.CompletedAt is null);
        var untouched = aggregate.Items.Single(item => item.CompletedAt is not null);

        var untouchedRowBefore = tracked.Items.Single(item => item.Id == untouched.Id);
        string untouchedTitleBefore = untouchedRowBefore.Title;
        var untouchedCompletedAtBefore = untouchedRowBefore.CompletedAt;

        var completedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        aggregate.CompleteItem(open.Id, completedAt);

        _mapper.WriteTo(aggregate, tracked);

        tracked.Items.Single(item => item.Id == open.Id).CompletedAt.ShouldBe(completedAt);

        var untouchedRowAfter = tracked.Items.Single(item => item.Id == untouched.Id);
        untouchedRowAfter.ShouldBeSameAs(untouchedRowBefore);
        untouchedRowAfter.Title.ShouldBe(untouchedTitleBefore);
        untouchedRowAfter.CompletedAt.ShouldBe(untouchedCompletedAtBefore);
    }

    [Fact]
    public void WriteTo_ReconcilesTagsAsASetAndReportsAChange()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);
        var tagged = aggregate.Items.Single(item => item.Tags.Count > 0);

        aggregate.AddTagToItem(tagged.Id, "Errand");
        aggregate.RemoveTagFromItem(tagged.Id, ATodoListAggregate.CompletedItemTags[0]);

        _mapper.WriteTo(aggregate, tracked).ShouldBeTrue();

        tracked.Items.Single(item => item.Id == tagged.Id).Tags
            .Select(tag => tag.Value)
            .ShouldBe(tagged.Tags.Select(tag => tag.Value), ignoreOrder: true);
    }

    [Fact]
    public void WriteTo_ReportsNoChange_WhenATagIsReAdded()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);
        var tagged = aggregate.Items.Single(item => item.Tags.Count > 0);

        aggregate.AddTagToItem(tagged.Id, ATodoListAggregate.CompletedItemTags[0]);

        _mapper.WriteTo(aggregate, tracked).ShouldBeFalse();
    }

    [Fact]
    public void WriteTo_EmptiesTheRowsCollection_WhenEveryItemIsRemoved()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);

        foreach (var itemId in aggregate.Items.Select(item => item.Id).ToList())
        {
            aggregate.RemoveItem(itemId);
        }

        _mapper.WriteTo(aggregate, tracked).ShouldBeTrue();

        tracked.Items.ShouldBeEmpty();
    }

    [Fact]
    public void WriteTo_KeepsBothRows_WhenTwoItemsSwapTitles()
    {
        var stored = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(stored);
        var rowsBefore = tracked.Items.ToList();

        var swapped = WithTheTwoTitlesExchanged(stored);

        _mapper.WriteTo(swapped, tracked);

        tracked.Items.ShouldBe(rowsBefore, ignoreOrder: true, "the rows themselves must survive a rename");

        tracked.Items.Single(item => item.Id == ATodoListAggregate.CompletedItemId).Title
            .ShouldBe(ATodoListAggregate.OpenItemTitle);
        tracked.Items.Single(item => item.Id == ATodoListAggregate.OpenItemId).Title
            .ShouldBe(ATodoListAggregate.CompletedItemTitle);
    }

    [Fact]
    public void WriteTo_ReportsNoChange_WhenATagIsReAddedInADifferentCase()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);
        var tagged = aggregate.Items.Single(item => item.Tags.Count > 0);

        aggregate.AddTagToItem(tagged.Id, ATodoListAggregate.CompletedItemTags[0].ToUpperInvariant());

        _mapper.WriteTo(aggregate, tracked).ShouldBeFalse();

        tracked.Items.Single(item => item.Id == tagged.Id).Tags
            .Select(tag => tag.Value)
            .ShouldBe(ATodoListAggregate.CompletedItemTags, ignoreOrder: true);
    }

    [Fact]
    public void WriteTo_ReplacesAStoredTagThatIsNotNormalised()
    {
        var aggregate = ATodoListAggregate.FullyPopulated();
        var tracked = StoredRowFor(aggregate);
        var tagged = aggregate.Items.Single(item => item.Tags.Count > 0);

        var row = tracked.Items.Single(item => item.Id == tagged.Id);
        var stored = row.Tags.Single(tag => tag.Value == ATodoListAggregate.CompletedItemTags[0]);
        stored.Value = stored.Value.ToUpperInvariant();

        _mapper.WriteTo(aggregate, tracked).ShouldBeTrue();

        row.Tags.Select(tag => tag.Value)
            .ShouldBe(tagged.Tags.Select(tag => tag.Value), ignoreOrder: true);
    }

    private static TodoList WithTheTwoTitlesExchanged(TodoList stored)
    {
        var completed = stored.Items.Single(item => item.Id == ATodoListAggregate.CompletedItemId);
        var open = stored.Items.Single(item => item.Id == ATodoListAggregate.OpenItemId);

        return TodoList.Rehydrate(
            stored.Id,
            stored.OwnerId,
            stored.Name.Value,
            [
                TodoItem.Rehydrate(
                    completed.Id,
                    completed.TodoListId,
                    open.Title.Value,
                    completed.Description,
                    completed.CompletedAt,
                    completed.Tags.Select(tag => tag.Value)),
                TodoItem.Rehydrate(
                    open.Id,
                    open.TodoListId,
                    completed.Title.Value,
                    open.Description,
                    open.CompletedAt,
                    open.Tags.Select(tag => tag.Value)),
            ]);
    }

    private const uint _storedVersion = 4_242u;

    private static readonly Guid _storedCreatedBy = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid _storedLastModifiedBy = new("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset _storedCreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _storedLastModifiedAt = new(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);

    private TodoListRecord StoredRowFor(TodoList aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);

        record.Version = _storedVersion;
        record.CreatedAt = _storedCreatedAt;
        record.CreatedBy = _storedCreatedBy;
        record.LastModifiedAt = _storedLastModifiedAt;
        record.LastModifiedBy = _storedLastModifiedBy;

        return record;
    }
}
