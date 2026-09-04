using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// Builds real aggregates, never fakes: a root that accepted anything would make the
/// "duplicate title becomes a conflict" tests assert nothing.
/// </summary>
internal static class ATodoList
{
    /// <summary>The creation event is already cleared, so callers assert on their own events only.</summary>
    internal static TodoList OwnedBy(Guid ownerId, string name = "Groceries")
    {
        var list = TodoList.Create(ownerId, name, StubDateTimeProvider.DefaultInstant);
        list.ClearDomainEvents();

        return list;
    }

    internal static TodoList OwnedBySomebodyElseThan(Guid notThisUserId)
    {
        var otherOwnerId = Guid.CreateVersion7();

        if (otherOwnerId == notThisUserId)
        {
            throw new InvalidOperationException("Guid.CreateVersion7 produced a collision.");
        }

        return OwnedBy(otherOwnerId, "Somebody else's list");
    }

    internal static TodoList OwnedByWithItem(Guid ownerId, out Guid itemId, string title = "Buy milk")
    {
        var list = OwnedBy(ownerId);
        itemId = list.AddItem(title, null);
        list.ClearDomainEvents();

        return list;
    }

    /// <summary>
    /// A list with one item, placed at <paramref name="version"/> the way the store places a
    /// freshly loaded aggregate. It goes through <see cref="IVersioned"/> because that is the only
    /// way anything writes a version.
    /// </summary>
    internal static TodoList OwnedByWithItemAtVersion(Guid ownerId, uint version, out Guid itemId)
    {
        var list = OwnedByWithItem(ownerId, out itemId);
        ((IVersioned)list).SetVersion(version);

        return list;
    }

    /// <summary>Filled to <see cref="TodoList.MaxItems"/>.</summary>
    internal static TodoList OwnedByAndFull(Guid ownerId)
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
