using System.Globalization;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Model.Entities;

public sealed class TodoListTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoList ANewList(string name = "Groceries") =>
        TodoList.Create(UserId.Create(Guid.CreateVersion7()), name, _now);

    #region Creation

    [Fact]
    public void Create_AssignsAnIdAnOwnerAndAName()
    {
        var ownerId = UserId.Create(Guid.CreateVersion7());

        var list = TodoList.Create(ownerId, "Groceries", _now);

        list.Id.ShouldNotBe(Guid.Empty);
        list.OwnerId.ShouldBe(ownerId);
        list.Name.Value.ShouldBe("Groceries");
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Create_NormalisesTheName() =>
        TodoList.Create(UserId.Create(Guid.CreateVersion7()), "  Groceries  ", _now).Name.Value.ShouldBe("Groceries");

    [Fact]
    public void Create_Rejects_AnAbsentOwner()
    {
        var exception = Should.Throw<ArgumentNullException>(() => TodoList.Create(null!, "Groceries", _now));

        exception.ParamName.ShouldBe("ownerId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_ABlankName(string name) =>
        Should.Throw<DomainException>(() => TodoList.Create(UserId.Create(Guid.CreateVersion7()), name, _now));

    [Fact]
    public void Create_Rejects_ANameLongerThanTheMaximum() =>
        Should.Throw<DomainException>(
            () => TodoList.Create(UserId.Create(Guid.CreateVersion7()), new string('a', TodoListName.MaxLength + 1), _now));

    [Fact]
    public void Create_GivesEveryListADistinctId() =>
        ANewList().Id.ShouldNotBe(ANewList().Id);

    [Fact]
    public void TheConcurrencyToken_HasNoPubliclyReachableSetter()
    {
        var setter = typeof(TodoList).GetProperty(nameof(TodoList.Version))!.SetMethod;

        (setter is null || !setter.IsPublic).ShouldBeTrue(
            "'Version' must not expose a public setter.");
    }

    [Fact]
    public void TheItemCap_IsFiveHundredItems() => TodoList.MaxItems.ShouldBe(500);

    #endregion

    #region Rename

    [Fact]
    public void Rename_ReplacesAndNormalisesTheName()
    {
        var list = ANewList();

        list.Rename("  Shopping  ");

        list.Name.Value.ShouldBe("Shopping");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Rename_Rejects_ABlankName(string name)
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.Rename(name));
        list.Name.Value.ShouldBe("Groceries");
    }

    [Fact]
    public void Rename_Rejects_ANameLongerThanTheMaximum()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.Rename(new string('a', TodoListName.MaxLength + 1)));
        list.Name.Value.ShouldBe("Groceries");
    }

    #endregion

    #region Adding items

    [Fact]
    public void AddItem_AppendsAnItemAndReturnsItsId()
    {
        var list = ANewList();

        var itemId = list.AddItem("Buy milk", "Semi-skimmed");

        var item = list.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(itemId);
        item.Title.Value.ShouldBe("Buy milk");
        item.Description.ShouldBe("Semi-skimmed");
        item.TodoListId.ShouldBe(list.Id);
        item.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void AddItem_Rejects_ADuplicateTitle()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);

        var exception = Should.Throw<DomainException>(() => list.AddItem("Buy milk", null));

        exception.Message.ShouldContain("Buy milk");
        list.Items.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("BUY MILK")]
    [InlineData("buy milk")]
    [InlineData("Buy Milk")]
    public void AddItem_Rejects_ATitleThatDiffersOnlyByCase(string duplicate)
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);

        Should.Throw<DomainException>(() => list.AddItem(duplicate, null));
        list.Items.Count.ShouldBe(1);
    }

    [Fact]
    public void AddItem_Rejects_ATitleThatDiffersOnlyBySurroundingWhitespace()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);

        Should.Throw<DomainException>(() => list.AddItem("   Buy milk  ", null));
        list.Items.Count.ShouldBe(1);
    }

    [Fact]
    public void AddItem_Accepts_ATitleThatDiffersInMoreThanCase()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);

        list.AddItem("Buy milk twice", null);

        list.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void AddItem_Accepts_ATitleFreedByRemovingTheItemThatHeldIt()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.RemoveItem(itemId);

        list.AddItem("Buy milk", null);

        list.Items.ShouldHaveSingleItem().Title.Value.ShouldBe("Buy milk");
    }

    [Fact]
    public void AddItem_Rejects_AnItemBeyondTheMaximumCount()
    {
        var list = ANewList();

        for (int index = 0; index < TodoList.MaxItems; index++)
        {
            list.AddItem($"item-{index}", null);
        }

        var exception = Should.Throw<DomainException>(() => list.AddItem("one too many", null));

        exception.Message.ShouldContain(TodoList.MaxItems.ToString(CultureInfo.InvariantCulture));
        list.Items.Count.ShouldBe(TodoList.MaxItems);
    }

    [Fact]
    public void AddItem_Accepts_TheItemThatFillsTheListExactly()
    {
        var list = ANewList();

        for (int index = 0; index < TodoList.MaxItems; index++)
        {
            list.AddItem($"item-{index}", null);
        }

        list.Items.Count.ShouldBe(TodoList.MaxItems);
    }

    [Fact]
    public void AddItem_Accepts_AnItemAfterRoomIsMadeOnAFullList()
    {
        var list = ANewList();

        for (int index = 0; index < TodoList.MaxItems; index++)
        {
            list.AddItem($"item-{index}", null);
        }

        list.RemoveItem(list.Items.First().Id);
        list.AddItem("room was made", null);

        list.Items.Count.ShouldBe(TodoList.MaxItems);
        list.Items.ShouldContain(item => item.Title.Value == "room was made");
    }

    #endregion

    #region Removing items

    [Fact]
    public void RemoveItem_DetachesTheItemFromTheList()
    {
        var list = ANewList();
        var keptId = list.AddItem("Keep me", null);
        var removedId = list.AddItem("Remove me", null);

        list.RemoveItem(removedId);

        list.Items.ShouldHaveSingleItem().Id.ShouldBe(keptId);
    }

    [Fact]
    public void RemoveItem_Rejects_AnUnknownItemId()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);
        var unknownId = Guid.CreateVersion7();

        var exception = Should.Throw<DomainException>(() => list.RemoveItem(unknownId));

        exception.Message.ShouldContain(unknownId.ToString());
        list.Items.Count.ShouldBe(1);
    }

    [Fact]
    public void RemoveItem_Rejects_AnItemIdBelongingToAnotherList()
    {
        var list = ANewList();
        var other = ANewList("Other");
        var foreignItemId = other.AddItem("Not mine", null);

        Should.Throw<DomainException>(() => list.RemoveItem(foreignItemId));
    }

    #endregion

    #region Completing and reopening items

    [Fact]
    public void CompleteItem_MarksTheItemCompletedAtTheGivenInstant()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        var completedAt = _now.AddHours(3);

        list.CompleteItem(itemId, completedAt);

        var item = list.Items.ShouldHaveSingleItem();
        item.IsCompleted.ShouldBeTrue();
        item.CompletedAt.ShouldBe(completedAt);
    }

    [Fact]
    public void CompleteItem_Rejects_AnAlreadyCompletedItem()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        var firstCompletion = _now.AddHours(1);
        list.CompleteItem(itemId, firstCompletion);

        Should.Throw<DomainException>(() => list.CompleteItem(itemId, _now.AddHours(2)));

        list.Items.ShouldHaveSingleItem().CompletedAt.ShouldBe(firstCompletion);
    }

    [Fact]
    public void CompleteItem_RaisesNoEvent_WhenTheItemRefusesTheChange()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.CompleteItem(itemId, _now);
        list.ClearDomainEvents();

        Should.Throw<DomainException>(() => list.CompleteItem(itemId, _now.AddHours(1)));

        list.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void CompleteItem_Rejects_AnUnknownItemId()
    {
        var list = ANewList();
        list.ClearDomainEvents();

        Should.Throw<DomainException>(() => list.CompleteItem(Guid.CreateVersion7(), _now));

        list.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ReopenItem_ClearsTheCompletion()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.CompleteItem(itemId, _now);

        list.ReopenItem(itemId, _now);

        var item = list.Items.ShouldHaveSingleItem();
        item.IsCompleted.ShouldBeFalse();
        item.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void ReopenItem_AllowsTheItemToBeCompletedAgain()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.CompleteItem(itemId, _now);
        list.ReopenItem(itemId, _now);

        var secondCompletion = _now.AddDays(1);
        list.CompleteItem(itemId, secondCompletion);

        list.Items.ShouldHaveSingleItem().CompletedAt.ShouldBe(secondCompletion);
    }

    [Fact]
    public void ReopenItem_IsANoOp_OnAnItemThatIsAlreadyOpen()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        list.ReopenItem(itemId, _now);

        list.Items.ShouldHaveSingleItem().IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void ReopenItem_Rejects_AnUnknownItemId()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.ReopenItem(Guid.CreateVersion7(), _now));
    }

    #endregion

    #region Renaming and describing items

    [Fact]
    public void UpdateItem_ReplacesTheTitleAndDescription()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", "Semi-skimmed");

        list.UpdateItem(itemId, "Buy bread", "Wholemeal");

        var item = list.Items.ShouldHaveSingleItem();
        item.Title.Value.ShouldBe("Buy bread");
        item.Description.ShouldBe("Wholemeal");
    }

    [Fact]
    public void UpdateItem_Accepts_RenamingAnItemToItsOwnCurrentTitle()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        Should.NotThrow(() => list.UpdateItem(itemId, "Buy milk", null));

        list.Items.ShouldHaveSingleItem().Title.Value.ShouldBe("Buy milk");
    }

    [Fact]
    public void UpdateItem_Accepts_RenamingAnItemToItsOwnTitleInADifferentCase()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        Should.NotThrow(() => list.UpdateItem(itemId, "BUY MILK", null));

        list.Items.ShouldHaveSingleItem().Title.Value.ShouldBe("BUY MILK");
    }

    [Fact]
    public void UpdateItem_Rejects_ATitleAlreadyHeldByAnotherItem()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);
        var itemId = list.AddItem("Buy bread", null);

        var exception = Should.Throw<DomainException>(() => list.UpdateItem(itemId, "Buy milk", null));

        exception.Message.ShouldContain("Buy milk");
        list.Items.Single(item => item.Id == itemId).Title.Value.ShouldBe("Buy bread");
    }

    [Fact]
    public void UpdateItem_Rejects_AWhitespaceVariantOfATitleAlreadyHeldByAnotherItem()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);
        var itemId = list.AddItem("Buy bread", "Wholemeal");

        Should.Throw<DomainException>(() => list.UpdateItem(itemId, "  buy milk ", "Changed"));

        var item = list.Items.Single(item => item.Id == itemId);
        item.Title.Value.ShouldBe("Buy bread");
        item.Description.ShouldBe("Wholemeal");
    }

    [Fact]
    public void UpdateItem_StoresTheTrimmedTitle_AndTheListRehydrates()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);
        var itemId = list.AddItem("Buy bread", null);

        list.UpdateItem(itemId, "  Buy eggs ", null);

        list.Items.Single(item => item.Id == itemId).Title.Value.ShouldBe("Buy eggs");
        var reloaded = TodoList.Rehydrate(
            list.Id,
            list.OwnerId,
            list.Name.Value,
            list.Items.Select(item => TodoItem.Rehydrate(
                item.Id, list.Id, item.Title.Value, item.Description, item.CompletedAt, [])));
        reloaded.Items.Select(item => item.Title.Value).ShouldBe(["Buy milk", "Buy eggs"]);
    }

    [Fact]
    public void UpdateItem_ChangesNothing_WhenTheDescriptionIsRefused()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", "Semi-skimmed");

        Should.Throw<DomainException>(() =>
            list.UpdateItem(itemId, "Buy bread", new string('x', TodoItem.MaxDescriptionLength + 1)));

        var item = list.Items.ShouldHaveSingleItem();
        item.Title.Value.ShouldBe("Buy milk");
        item.Description.ShouldBe("Semi-skimmed");
    }

    [Fact]
    public void UpdateItem_Rejects_AnUnknownItemId()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.UpdateItem(Guid.CreateVersion7(), "Buy milk", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateItem_Rejects_ABlankTitle(string title)
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        Should.Throw<DomainException>(() => list.UpdateItem(itemId, title, null));
        list.Items.ShouldHaveSingleItem().Title.Value.ShouldBe("Buy milk");
    }

    #endregion

    #region Replacing an item's tags

    [Fact]
    public void SetItemTags_ReplacesTheWholeSet()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");

        list.SetItemTags(itemId, ["shopping", "weekly"]);

        list.Items.ShouldHaveSingleItem().Tags.Select(tag => tag.Value).ShouldBe(["shopping", "weekly"]);
    }

    [Fact]
    public void SetItemTags_KeepsATagPresentInBothTheOldAndTheNewSet()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");
        list.AddTagToItem(itemId, "shopping");

        list.SetItemTags(itemId, ["urgent", "weekly"]);

        list.Items.ShouldHaveSingleItem().Tags.Select(tag => tag.Value).ShouldBe(["urgent", "weekly"], ignoreOrder: true);
    }

    [Fact]
    public void SetItemTags_Accepts_AnEmptySetAndClearsTheTags()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");

        list.SetItemTags(itemId, []);

        list.Items.ShouldHaveSingleItem().Tags.ShouldBeEmpty();
    }

    [Fact]
    public void SetItemTags_Rejects_AnUnknownItemId()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.SetItemTags(Guid.CreateVersion7(), ["urgent"]));
    }

    [Fact]
    public void SetItemTags_Rejects_ANullTagCollection()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        Should.Throw<ArgumentNullException>(() => list.SetItemTags(itemId, null!));
    }

    #endregion

    #region Tagging items

    [Fact]
    public void AddTagToItem_AttachesTheNormalisedTag()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        list.AddTagToItem(itemId, "  Urgent  ");

        list.Items.ShouldHaveSingleItem().Tags.ShouldHaveSingleItem().Value.ShouldBe("urgent");
    }

    [Fact]
    public void AddTagToItem_IsIdempotent()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        list.AddTagToItem(itemId, "urgent");
        list.AddTagToItem(itemId, "urgent");

        list.Items.ShouldHaveSingleItem().Tags.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("URGENT")]
    [InlineData(" Urgent ")]
    [InlineData("uRgEnT")]
    public void AddTagToItem_TreatsDifferentCasingsAsTheSameTag(string variant)
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");

        list.AddTagToItem(itemId, variant);

        list.Items.ShouldHaveSingleItem().Tags.Count.ShouldBe(1);
    }

    [Fact]
    public void AddTagToItem_KeepsDistinctTagsSideBySide()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        list.AddTagToItem(itemId, "urgent");
        list.AddTagToItem(itemId, "shopping");

        list.Items.ShouldHaveSingleItem().Tags.Select(tag => tag.Value)
            .ShouldBe(["urgent", "shopping"]);
    }

    [Fact]
    public void RemoveTagFromItem_DetachesTheTag()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");
        list.AddTagToItem(itemId, "shopping");

        list.RemoveTagFromItem(itemId, "URGENT");

        list.Items.ShouldHaveSingleItem().Tags.ShouldHaveSingleItem().Value.ShouldBe("shopping");
    }

    [Fact]
    public void RemoveTagFromItem_IsIdempotent()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");

        list.RemoveTagFromItem(itemId, "urgent");
        list.RemoveTagFromItem(itemId, "urgent");

        list.Items.ShouldHaveSingleItem().Tags.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTagToItem_Rejects_ABlankTag(string tag)
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        Should.Throw<DomainException>(() => list.AddTagToItem(itemId, tag));
        list.Items.ShouldHaveSingleItem().Tags.ShouldBeEmpty();
    }

    [Fact]
    public void AddTagToItem_Rejects_AnUnknownItemId()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.AddTagToItem(Guid.CreateVersion7(), "urgent"));
    }

    [Fact]
    public void RemoveTagFromItem_Rejects_AnUnknownItemId()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.RemoveTagFromItem(Guid.CreateVersion7(), "urgent"));
    }

    #endregion

    #region Encapsulation

    [Fact]
    public void Items_IsExposedAsAReadOnlyCollection() =>
        typeof(TodoList).GetProperty(nameof(TodoList.Items))!.PropertyType
            .ShouldBe(typeof(IReadOnlyCollection<TodoItem>));

    [Fact]
    public void Items_CannotBeMutatedThroughTheExposedCollection()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        var existingItem = list.Items.Single();

        var asCollection = (ICollection<TodoItem>)list.Items;

        asCollection.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => asCollection.Clear());
        Should.Throw<NotSupportedException>(() => asCollection.Remove(existingItem));
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(itemId);
    }

    [Fact]
    public void TheRoot_IsTheOnlyWayToPutAnItemOnAList()
    {
        typeof(TodoList).GetConstructors().ShouldBeEmpty();
        typeof(TodoItem).GetConstructors().ShouldBeEmpty();
    }

    #endregion

    #region Domain events

    [Fact]
    public void Create_RaisesExactlyOneCreationEvent()
    {
        var list = ANewList();

        list.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TodoListCreatedDomainEvent>();
    }

    [Fact]
    public void CompleteItem_RaisesACompletionEvent()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.ClearDomainEvents();

        list.CompleteItem(itemId, _now);

        list.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TodoItemCompletedDomainEvent>();
    }

    [Fact]
    public void TheNonCompletionOperations_RaiseNoEvents()
    {
        var list = ANewList();
        list.ClearDomainEvents();

        var itemId = list.AddItem("Buy milk", null);
        list.Rename("Shopping");
        list.AddTagToItem(itemId, "urgent");
        list.RemoveTagFromItem(itemId, "urgent");
        list.UpdateItem(itemId, "Buy bread", "Wholemeal");
        list.SetItemTags(itemId, ["weekly"]);
        list.ReopenItem(itemId, _now);
        list.RemoveItem(itemId);

        list.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ReopenItem_RaisesAReopenedEvent_WhenTheItemWasCompleted()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.CompleteItem(itemId, _now);
        list.ClearDomainEvents();

        list.ReopenItem(itemId, _now);

        list.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TodoItemReopenedDomainEvent>();
    }

    [Fact]
    public void ReopenItem_RaisesNoEvent_WhenTheItemWasAlreadyOpen()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.ClearDomainEvents();

        list.ReopenItem(itemId, _now);

        list.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheEventsRaisedByTheAggregate()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.CompleteItem(itemId, _now);
        list.DomainEvents.Count.ShouldBe(2);

        list.ClearDomainEvents();

        list.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void DomainEvents_AccumulateInTheOrderTheyHappened()
    {
        var list = ANewList();
        var first = list.AddItem("Buy milk", null);
        var second = list.AddItem("Buy bread", null);

        list.CompleteItem(first, _now);
        list.CompleteItem(second, _now.AddMinutes(1));

        list.DomainEvents.Select(domainEvent => domainEvent.GetType()).ShouldBe(
        [
            typeof(TodoListCreatedDomainEvent),
            typeof(TodoItemCompletedDomainEvent),
            typeof(TodoItemCompletedDomainEvent),
        ]);
    }

    #endregion

    #region Identity

    [Fact]
    public void TwoDistinctLists_AreNotEqual()
    {
        var left = ANewList();
        var right = ANewList();

        left.Equals(right).ShouldBeFalse();
    }

    #endregion
}
