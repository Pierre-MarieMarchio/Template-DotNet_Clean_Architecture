using System.Globalization;
using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.Entities;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Entities;

/// <summary>
/// The seam a persistence layer uses to turn stored values back into an aggregate.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the persistence layer keeps a model of its own and therefore has to reconstruct the
/// aggregate itself. The alternative was reflection over private members, which fails at runtime when a
/// property is renamed rather than at compile time — the exact mechanism this template was rescued from.
/// </para>
/// <para>
/// What these tests defend is the difference between <c>Rehydrate</c> and <c>Create</c>. Recalling a stored
/// fact is not the same act as deciding a new one: it takes the id it is given rather than minting one, and
/// it raises no domain event. A <c>Rehydrate</c> that raised the creation event would re-publish it on every
/// single load.
/// </para>
/// </remarks>
public sealed class TodoListRehydrationTests
{
    private static readonly Guid _listId = new("0199a3c4-1111-7000-8000-000000000001");
    private static readonly Guid _ownerId = new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60");
    private static readonly Guid _itemId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid _otherListId = new("0199a3c4-1111-7000-8000-000000000002");

    [Fact]
    public void Rehydrate_KeepsTheStoredIdentity()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.Id.ShouldBe(_listId);
        list.OwnerId.ShouldBe(_ownerId);
        list.Name.Value.ShouldBe("Groceries");
        list.Items.ShouldBeEmpty();
    }

    /// <summary>
    /// The single most important difference from <see cref="TodoList.Create"/>. Loading a list is not
    /// creating one, and an event raised here would be delivered again on every read.
    /// </summary>
    [Fact]
    public void Rehydrate_RaisesNoDomainEvent()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.DomainEvents.ShouldBeEmpty();
    }

    /// <summary>Whereas creation does — so the assertion above is a contrast, not a tautology.</summary>
    [Fact]
    public void Create_ByContrast_RaisesOne()
    {
        var list = TodoList.Create(_ownerId, "Groceries", DateTimeOffset.UnixEpoch);

        list.DomainEvents.ShouldHaveSingleItem();
    }

    [Fact]
    public void Rehydrate_RestoresItemsWithTheirTagsAndCompletion()
    {
        var completedAt = new DateTimeOffset(2026, 3, 6, 11, 12, 13, TimeSpan.Zero);

        var item = TodoItem.Rehydrate(_itemId, _listId, "Buy milk", "Two litres", completedAt, ["urgent", "shop"]);
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", [item]);

        var restored = list.Items.ShouldHaveSingleItem();

        restored.Id.ShouldBe(_itemId);
        restored.TodoListId.ShouldBe(_listId);
        restored.Title.Value.ShouldBe("Buy milk");
        restored.Description.ShouldBe("Two litres");
        restored.CompletedAt.ShouldBe(completedAt);
        restored.IsCompleted.ShouldBeTrue();
        restored.Tags.Select(tag => tag.Value).ShouldBe(["urgent", "shop"], ignoreOrder: true);
    }

    /// <summary>
    /// Values go through the same factories a live aggregate uses, so a row that no longer satisfies an
    /// invariant is refused on the way in rather than becoming an aggregate that cannot honour its own
    /// rules.
    /// </summary>
    [Fact]
    public void Rehydrate_RefusesAStoredNameThatViolatesTheInvariant()
    {
        Should.Throw<DomainException>(() => TodoList.Rehydrate(_listId, _ownerId, "   ", []));
    }

    [Fact]
    public void Rehydrate_RefusesAnEmptyId()
    {
        Should.Throw<DomainException>(() => TodoList.Rehydrate(Guid.Empty, _ownerId, "Groceries", []));
    }

    [Fact]
    public void Rehydrate_RefusesAnEmptyOwner()
    {
        Should.Throw<DomainException>(() => TodoList.Rehydrate(_listId, Guid.Empty, "Groceries", []));
    }

    [Fact]
    public void RehydrateItem_RefusesAnEmptyId()
    {
        Should.Throw<DomainException>(
            () => TodoItem.Rehydrate(Guid.Empty, _listId, "Buy milk", null, null, []));
    }

    [Fact]
    public void RehydrateItem_RefusesAnEmptyListId()
    {
        Should.Throw<DomainException>(
            () => TodoItem.Rehydrate(_itemId, Guid.Empty, "Buy milk", null, null, []));
    }

    [Fact]
    public void RehydrateItem_NormalisesTagsLikeALiveItemWould()
    {
        var item = TodoItem.Rehydrate(_itemId, _listId, " Buy milk ", "  ", null, [" URGENT "]);

        item.Title.Value.ShouldBe("Buy milk");
        item.Description.ShouldBeNull();
        item.Tags.ShouldHaveSingleItem().Value.ShouldBe("urgent");
    }

    /// <summary>
    /// A rehydrated aggregate is fully alive: the invariants that span its items are enforced against the
    /// items it was given, not against an empty collection. An aggregate loaded without its children would
    /// silently accept a duplicate title.
    /// </summary>
    [Fact]
    public void ARehydratedAggregate_EnforcesItsInvariantsAgainstTheRestoredItems()
    {
        var item = TodoItem.Rehydrate(_itemId, _listId, "Buy milk", null, null, []);
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", [item]);

        Should.Throw<DomainException>(() => list.AddItem("buy milk", null));
    }

    /// <summary>
    /// The load path goes through the same gate <c>AddItem</c> does, so a stored set that breaks a
    /// list-level invariant is refused rather than becoming an aggregate that cannot honour its own
    /// rules. Bypassing the gate — appending the items straight to the backing list — turns each of
    /// the four tests below red.
    /// </summary>
    [Fact]
    public void Rehydrate_RefusesMoreItemsThanTheCap()
    {
        var items = ItemsNumbering(TodoList.MaxItems + 1);

        var exception = Should.Throw<DomainException>(
            () => TodoList.Rehydrate(_listId, _ownerId, "Groceries", items));

        exception.Message.ShouldContain(TodoList.MaxItems.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Rehydrate_Accepts_ExactlyTheCap() =>
        TodoList.Rehydrate(_listId, _ownerId, "Groceries", ItemsNumbering(TodoList.MaxItems))
            .Items.Count.ShouldBe(TodoList.MaxItems);

    [Theory]
    [InlineData("buy milk")]
    [InlineData("BUY MILK")]
    [InlineData("  Buy milk  ")]
    public void Rehydrate_RefusesTwoItemsWhoseTitlesOnlyDifferByCaseOrPadding(string duplicate)
    {
        var first = AnItemTitled("Buy milk");
        var second = AnItemTitled(duplicate);

        var exception = Should.Throw<DomainException>(
            () => TodoList.Rehydrate(_listId, _ownerId, "Groceries", [first, second]));

        exception.Message.ShouldContain("already contains");
    }

    /// <summary>
    /// A row read with the wrong filter would otherwise be adopted by a list that does not own it,
    /// and the next save would write it back under that list's id.
    /// </summary>
    [Fact]
    public void Rehydrate_RefusesAnItemBelongingToAnotherList()
    {
        var foreign = TodoItem.Rehydrate(_itemId, _otherListId, "Buy milk", null, null, []);

        var exception = Should.Throw<DomainException>(
            () => TodoList.Rehydrate(_listId, _ownerId, "Groceries", [foreign]));

        exception.Message.ShouldContain(_otherListId.ToString());
    }

    [Fact]
    public void Rehydrate_RefusesANullItem()
    {
        Should.Throw<DomainException>(
            () => TodoList.Rehydrate(_listId, _ownerId, "Groceries", [null!]));
    }

    /// <summary>
    /// The audit values are absent from the signature on purpose, so a load leaves them unset until the
    /// store stamps them. A <c>Rehydrate</c> that invented a creation date would make every loaded
    /// aggregate claim to have been created at the moment it was read.
    /// </summary>
    [Fact]
    public void ARehydratedList_CarriesNoAuditStampsUntilTheStoreWritesThem()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.CreatedAt.ShouldBe(default);
        list.CreatedBy.ShouldBeNull();
        list.LastModifiedAt.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();

        var storedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ((IAuditable)list).SetCreated(storedAt, _ownerId);

        list.CreatedAt.ShouldBe(storedAt);
        list.CreatedBy.ShouldBe(_ownerId);
        list.LastModifiedAt.ShouldBeNull();
    }

    /// <summary>
    /// The concurrency token is written through an explicitly implemented interface, exactly as the audit
    /// values are: readable by anyone, assignable only by something that has declared itself to be the
    /// persistence layer.
    /// </summary>
    [Fact]
    public void TheVersion_IsWrittenThroughIVersionedAndReadableAsAProperty()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.Version.ShouldBe(0u);

        ((IVersioned)list).SetVersion(4_242u);

        list.Version.ShouldBe(4_242u);
        ((IVersioned)list).Version.ShouldBe(4_242u);
    }

    /// <summary>
    /// The reason it is an explicit implementation: application code holding a <c>TodoList</c> cannot reach
    /// the setter without first admitting, in writing, that it is doing something a store does.
    /// </summary>
    [Fact]
    public void TheVersionSetter_IsNotOnThePublicSurfaceOfTheAggregate()
    {
        typeof(TodoList).GetMethod(nameof(IVersioned.SetVersion))
            .ShouldBeNull("SetVersion must be reachable only through IVersioned, never as a public method.");

        typeof(TodoList).GetProperty(nameof(TodoList.Version))!
            .SetMethod!.IsPublic
            .ShouldBeFalse("Version is set by the store through IVersioned, not by assignment.");
    }

    private static TodoItem AnItemTitled(string title) =>
        TodoItem.Rehydrate(Guid.CreateVersion7(), _listId, title, null, null, []);

    private static List<TodoItem> ItemsNumbering(int count) =>
        [.. Enumerable.Range(0, count).Select(index => AnItemTitled($"item-{index}"))];
}
