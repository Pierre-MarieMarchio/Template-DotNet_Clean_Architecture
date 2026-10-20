using System.Globalization;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Model.Entities;

public sealed class TodoListRehydrationTests
{
    private static readonly Guid _listId = new("0199a3c4-1111-7000-8000-000000000001");
    private static readonly UserId _ownerId = UserId.Create(new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60"));
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

    [Fact]
    public void Rehydrate_RaisesNoDomainEvent()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.DomainEvents.ShouldBeEmpty();
    }

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

    [Fact]
    public void Rehydrate_RefusesAStoredNameThatViolatesTheInvariant() =>
        Should.Throw<DomainException>(() => TodoList.Rehydrate(_listId, _ownerId, "   ", []));

    [Fact]
    public void Rehydrate_RefusesAnEmptyId() =>
        Should.Throw<DomainException>(() => TodoList.Rehydrate(Guid.Empty, _ownerId, "Groceries", []));

    [Fact]
    public void Rehydrate_RefusesAnAbsentOwner() =>
        Should.Throw<ArgumentNullException>(() => TodoList.Rehydrate(_listId, null!, "Groceries", []));

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

    [Fact]
    public void ARehydratedAggregate_EnforcesItsInvariantsAgainstTheRestoredItems()
    {
        var item = TodoItem.Rehydrate(_itemId, _listId, "Buy milk", null, null, []);
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", [item]);

        Should.Throw<DomainException>(() => list.AddItem("buy milk", null));
    }

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
        Should.Throw<ArgumentNullException>(
            () => TodoList.Rehydrate(_listId, _ownerId, "Groceries", [null!]));
    }

    [Fact]
    public void ARehydratedList_CarriesNoAuditStampsUntilTheStoreWritesThem()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.CreatedAt.ShouldBe(default);
        list.CreatedBy.ShouldBeNull();
        list.LastModifiedAt.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();

        var storedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ((IAuditable)list).SetCreated(storedAt, _ownerId.Value);

        list.CreatedAt.ShouldBe(storedAt);
        list.CreatedBy.ShouldBe(_ownerId.Value);
        list.LastModifiedAt.ShouldBeNull();
    }

    [Fact]
    public void TheVersion_IsWrittenThroughIVersionedAndReadableAsAProperty()
    {
        var list = TodoList.Rehydrate(_listId, _ownerId, "Groceries", []);

        list.Version.ShouldBe(0u);

        ((IVersioned)list).SetVersion(4_242u);

        list.Version.ShouldBe(4_242u);
        ((IVersioned)list).Version.ShouldBe(4_242u);
    }

    [Fact]
    public void TheVersionSetter_IsNotOnThePublicSurfaceOfTheAggregate()
    {
        typeof(TodoList).GetMethod(nameof(IVersioned.SetVersion))
            .ShouldBeNull("SetVersion must be reachable only through IVersioned, never as a public method.");

        typeof(TodoList).GetProperty(nameof(TodoList.Version))!
            .GetSetMethod(nonPublic: false)
            .ShouldBeNull("Version is set by the store through IVersioned, not by assignment.");
    }

    private static TodoItem AnItemTitled(string title) =>
        TodoItem.Rehydrate(Guid.CreateVersion7(), _listId, title, null, null, []);

    private static List<TodoItem> ItemsNumbering(int count) =>
        [.. Enumerable.Range(0, count).Select(index => AnItemTitled($"item-{index}"))];
}
