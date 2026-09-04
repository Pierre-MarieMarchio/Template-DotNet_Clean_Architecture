using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Entities;

/// <summary>
/// Every test here drives the item through a <see cref="TodoList"/>, because that is the only
/// way to reach it: an item that could be changed directly would put the list's invariants out
/// of the list's reach.
/// </summary>
public sealed class TodoItemTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoList ANewList() => TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);

    private static TodoItem AnItem(string title = "Buy milk", string? description = null)
    {
        var list = ANewList();
        list.AddItem(title, description);

        return list.Items.Single();
    }

    #region Title

    [Fact]
    public void Title_IsTrimmed() => AnItem("  Buy milk  ").Title.Value.ShouldBe("Buy milk");

    [Fact]
    public void Title_KeepsItsInnerWhitespaceAndCase() =>
        AnItem("Buy  Milk AND bread").Title.Value.ShouldBe("Buy  Milk AND bread");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Title_Rejects_ABlankValue(string title)
    {
        var list = ANewList();

        var exception = Should.Throw<DomainException>(() => list.AddItem(title, null));

        exception.Message.ShouldContain("title");
    }

    [Fact]
    public void Title_Accepts_ExactlyTheMaximumLength() =>
        AnItem(new string('a', TodoItemTitle.MaxLength)).Title.Value.Length
            .ShouldBe(TodoItemTitle.MaxLength);

    [Fact]
    public void Title_Rejects_OneCharacterBeyondTheMaximumLength()
    {
        var list = ANewList();

        Should.Throw<DomainException>(() => list.AddItem(new string('a', TodoItemTitle.MaxLength + 1), null));
    }

    [Fact]
    public void Title_IsMeasuredAfterTrimming() =>
        AnItem($"  {new string('a', TodoItemTitle.MaxLength)}  ")
            .Title.Value.Length.ShouldBe(TodoItemTitle.MaxLength);

    #endregion

    #region Description

    [Fact]
    public void Description_IsTrimmed() =>
        AnItem("Buy milk", "  Semi-skimmed  ").Description.ShouldBe("Semi-skimmed");

    /// <summary>
    /// A blank description is absence, not an empty string: two representations of "there
    /// is no description" would have to be handled by every reader.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Description_IsNullWhenBlank(string? description) =>
        AnItem("Buy milk", description).Description.ShouldBeNull();

    [Fact]
    public void Description_Accepts_ExactlyTheMaximumLength() =>
        AnItem("Buy milk", new string('a', TodoItem.MaxDescriptionLength))
            .Description!.Length.ShouldBe(TodoItem.MaxDescriptionLength);

    [Fact]
    public void Description_Rejects_OneCharacterBeyondTheMaximumLength()
    {
        var list = ANewList();

        Should.Throw<DomainException>(
            () => list.AddItem("Buy milk", new string('a', TodoItem.MaxDescriptionLength + 1)));
    }

    [Fact]
    public void ARejectedDescription_LeavesTheListUnchanged()
    {
        var list = ANewList();

        Should.Throw<DomainException>(
            () => list.AddItem("Buy milk", new string('a', TodoItem.MaxDescriptionLength + 1)));

        list.Items.ShouldBeEmpty();
    }

    /// <summary>
    /// A tripwire on the value of the limit, not on the mechanism: every other length test above
    /// reads the constant and is therefore blind to a change in the constant itself. The column is
    /// sized from this number, so widening it is a schema decision and has to be a deliberate one.
    /// </summary>
    [Fact]
    public void TheMaximumDescriptionLength_IsTwoThousandCharacters() =>
        TodoItem.MaxDescriptionLength.ShouldBe(2000);

    #endregion

    #region Completion state

    [Fact]
    public void ANewItem_IsOpen()
    {
        var item = AnItem();

        item.IsCompleted.ShouldBeFalse();
        item.CompletedAt.ShouldBeNull();
    }

    /// <summary>
    /// <c>IsCompleted</c> is derived, not stored, so the flag and the timestamp can never
    /// disagree. Turning it into an independently settable field turns this red.
    /// </summary>
    [Fact]
    public void IsCompleted_TracksTheCompletionTimestamp()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        list.CompleteItem(itemId, _now);
        list.Items.Single().IsCompleted.ShouldBeTrue();
        list.Items.Single().CompletedAt.ShouldBe(_now);

        list.ReopenItem(itemId, _now);
        list.Items.Single().IsCompleted.ShouldBeFalse();
        list.Items.Single().CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void IsCompleted_HasNoSetter() =>
        typeof(TodoItem).GetProperty(nameof(TodoItem.IsCompleted))!.SetMethod.ShouldBeNull();

    /// <summary>
    /// The default instant is what an uninitialised caller passes. Accepting it would leave the item
    /// completed at 0001-01-01, a value no reader can tell apart from a real one.
    /// </summary>
    [Fact]
    public void Complete_Rejects_TheDefaultInstant()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.ClearDomainEvents();

        Should.Throw<DomainException>(() => list.CompleteItem(itemId, default));

        var item = list.Items.ShouldHaveSingleItem();
        item.IsCompleted.ShouldBeFalse();
        item.CompletedAt.ShouldBeNull();
        list.DomainEvents.ShouldBeEmpty();
    }

    #endregion

    #region Relationship to its list

    [Fact]
    public void AnItem_CarriesTheIdOfTheListThatOwnsIt()
    {
        var list = ANewList();
        list.AddItem("Buy milk", null);

        list.Items.Single().TodoListId.ShouldBe(list.Id);
    }

    /// <summary>
    /// A plain foreign key rather than a back-reference navigation, so the object graph stays a
    /// tree and cannot produce a serialisation cycle.
    /// </summary>
    [Fact]
    public void AnItem_HasNoBackReferenceToItsList() =>
        typeof(TodoItem).GetProperties()
            .ShouldNotContain(property => property.PropertyType == typeof(TodoList));

    [Fact]
    public void ItemsOnDifferentLists_AreDistinctEntities()
    {
        var left = ANewList();
        var right = ANewList();
        left.AddItem("Buy milk", null);
        right.AddItem("Buy milk", null);

        left.Items.Single().Equals(right.Items.Single()).ShouldBeFalse();
    }

    #endregion

    #region Tags

    [Fact]
    public void Tags_AreEmptyOnANewItem() => AnItem().Tags.ShouldBeEmpty();

    [Fact]
    public void Tags_IsExposedAsAReadOnlyCollection() =>
        typeof(TodoItem).GetProperty(nameof(TodoItem.Tags))!.PropertyType
            .ShouldBe(typeof(IReadOnlyCollection<Tag>));

    [Fact]
    public void Tags_CannotBeMutatedThroughTheExposedCollection()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);
        list.AddTagToItem(itemId, "urgent");
        var item = list.Items.Single();

        var asCollection = (ICollection<Tag>)item.Tags;

        asCollection.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => asCollection.Clear());
        item.Tags.Count.ShouldBe(1);
    }

    /// <summary>Value tripwire: widening the cap is an API change, not an implementation detail.</summary>
    [Fact]
    public void TheTagCap_IsTwentyTags() => TodoItem.MaxTags.ShouldBe(20);

    [Fact]
    public void Tags_AcceptExactlyTheCap()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        for (int i = 0; i < TodoItem.MaxTags; i++)
        {
            list.AddTagToItem(itemId, $"tag-{i}");
        }

        list.Items.Single().Tags.Count.ShouldBe(TodoItem.MaxTags);
    }

    [Fact]
    public void Tags_RefuseOneBeyondTheCap()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        for (int i = 0; i < TodoItem.MaxTags; i++)
        {
            list.AddTagToItem(itemId, $"tag-{i}");
        }

        Should.Throw<DomainException>(() => list.AddTagToItem(itemId, "one-too-many"));
    }

    /// <summary>
    /// Re-sending a tag the item already has stays a no-op even when the item is full: a retried
    /// request must not start failing just because the cap was reached.
    /// </summary>
    [Fact]
    public void AnExistingTag_IsStillANoOpOnAFullItem()
    {
        var list = ANewList();
        var itemId = list.AddItem("Buy milk", null);

        for (int i = 0; i < TodoItem.MaxTags; i++)
        {
            list.AddTagToItem(itemId, $"tag-{i}");
        }

        list.AddTagToItem(itemId, "tag-0");

        list.Items.Single().Tags.Count.ShouldBe(TodoItem.MaxTags);
    }

    #endregion

    #region Encapsulation

    /// <summary>
    /// Every mutator is internal to the domain assembly, which is what leaves the root as
    /// the only thing able to change an item. Making any of them public turns this red.
    /// </summary>
    [Theory]
    [InlineData("Complete")]
    [InlineData("Reopen")]
    [InlineData("AddTag")]
    [InlineData("RemoveTag")]
    [InlineData("ChangeTitle")]
    [InlineData("ChangeDescription")]
    public void TheMutators_AreNotPartOfThePublicSurface(string methodName) =>
        typeof(TodoItem).GetMethod(methodName).ShouldBeNull();

    [Fact]
    public void TheItemType_CannotBeConstructedFromOutsideTheDomain() =>
        typeof(TodoItem).GetConstructors().ShouldBeEmpty();

    [Fact]
    public void TheItemType_IsSealed() => typeof(TodoItem).IsSealed.ShouldBeTrue();

    #endregion
}
