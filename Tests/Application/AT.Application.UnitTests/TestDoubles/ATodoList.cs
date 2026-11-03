using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal static class ATodoList
{
    internal static TodoList OwnedBy(UserId ownerId, string name = "Groceries")
    {
        var list = TodoList.Create(ownerId, name, TheClock.DefaultInstant);
        list.ClearDomainEvents();

        return list;
    }

    internal static TodoList OwnedByWithItem(UserId ownerId, out Guid itemId, string title = "Buy milk")
    {
        var list = OwnedBy(ownerId);
        itemId = list.AddItem(title, null);
        list.ClearDomainEvents();

        return list;
    }

    internal static TodoList OwnedByWithItemAtVersion(UserId ownerId, uint version, out Guid itemId)
    {
        var list = OwnedByWithItem(ownerId, out itemId);
        ((IVersioned)list).SetVersion(version);

        return list;
    }

    internal static TodoList OwnedByAndFull(UserId ownerId)
    {
        var list = OwnedBy(ownerId);

        for (int index = 0; index < TodoList.MaxItems; index++)
        {
            list.AddItem($"item-{index}", null);
        }

        list.ClearDomainEvents();

        return list;
    }
}
