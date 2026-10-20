using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Model.Mapping;

public sealed class TodoListDtoMappingTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    [Fact]
    public void Detail_CarriesTheAggregatesOwnVersionAndAuditValues()
    {
        var list = TodoList.Create(_ownerId, "Groceries", _now);
        ((IVersioned)list).SetVersion(7);
        ((IAuditable)list).SetCreated(_now, _ownerId.Value);

        var projected = TodoListDtoMapping.Detail(list);

        projected.Version.ShouldBe(7u);
        projected.Value.Id.ShouldBe(list.Id);
        projected.Value.Name.ShouldBe("Groceries");
        projected.Value.CreatedAt.ShouldBe(_now);
    }

    [Fact]
    public void Detail_OrdersItemsByTitle_NotByInsertionOrder()
    {
        var list = TodoList.Create(_ownerId, "Groceries", _now);
        list.AddItem("Zebra", null);
        list.AddItem("Apple", null);
        list.AddItem("Mango", null);

        var projected = TodoListDtoMapping.Detail(list);

        projected.Value.Items.Select(item => item.Title).ShouldBe(["Apple", "Mango", "Zebra"]);
    }

    [Fact]
    public void Detail_MapsEveryItemField()
    {
        var list = TodoList.Create(_ownerId, "Groceries", _now);
        var itemId = list.AddItem("Buy milk", "Semi-skimmed");
        list.AddTagToItem(itemId, "urgent");
        list.CompleteItem(itemId, _now);

        var item = TodoListDtoMapping.Detail(list).Value.Items.ShouldHaveSingleItem();

        item.Id.ShouldBe(itemId);
        item.Title.ShouldBe("Buy milk");
        item.Description.ShouldBe("Semi-skimmed");
        item.IsCompleted.ShouldBeTrue();
        item.CompletedAt.ShouldBe(_now);
        item.Tags.ShouldBe(["urgent"]);
    }

    [Fact]
    public void Item_CarriesTheListsVersionRatherThanAnItemVersion()
    {
        var list = TodoList.Create(_ownerId, "Groceries", _now);
        var itemId = list.AddItem("Buy milk", null);
        ((IVersioned)list).SetVersion(42);

        var projected = TodoListDtoMapping.Item(list, itemId);

        projected.Version.ShouldBe(42u);
        projected.Value.Id.ShouldBe(itemId);
    }

    [Fact]
    public void Detail_Rejects_ANullList() =>
        Should.Throw<ArgumentNullException>(() => TodoListDtoMapping.Detail(null!));

    [Fact]
    public void Item_Rejects_ANullList() =>
        Should.Throw<ArgumentNullException>(() => TodoListDtoMapping.Item(null!, Guid.CreateVersion7()));
}
