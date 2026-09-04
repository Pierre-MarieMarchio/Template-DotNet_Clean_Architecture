using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.TodoLists.Entities;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

/// <summary>
/// Builds a to-do list aggregate in which <b>every</b> piece of state is set to a value distinguishable
/// from its type's default.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of the fidelity test that is easy to get wrong. A round-trip assertion that compares
/// <c>null</c> against <c>null</c>, or <c>default(DateTimeOffset)</c> against
/// <c>default(DateTimeOffset)</c>, passes for a property the mapper never copied — so a sample built from
/// a fresh aggregate would silently exempt exactly the nullable and optional properties most likely to be
/// forgotten. <see cref="TodoListMapperFidelityTests"/> therefore asserts that every property it compares
/// held a non-default value <em>before</em> comparing it, and this builder is what makes that possible.
/// </para>
/// <para>
/// It is also why the items differ from each other: one completed and tagged, one open with no description
/// and no tags. A mapper that hard-coded either shape would pass a single-item sample.
/// </para>
/// </remarks>
internal static class ATodoListAggregate
{
    internal static readonly Guid OwnerId = new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60");
    internal static readonly Guid CreatedBy = new("11111111-2222-3333-4444-555555555555");
    internal static readonly Guid LastModifiedBy = new("66666666-7777-8888-9999-aaaaaaaaaaaa");
    internal static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    internal static readonly DateTimeOffset LastModifiedAt = new(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
    internal static readonly DateTimeOffset CompletedAt = new(2026, 3, 6, 11, 12, 13, TimeSpan.Zero);

    /// <summary>Non-zero, so a mapper that dropped the concurrency token is visible.</summary>
    internal const uint Version = 987_654u;

    internal const string Name = "Groceries and other errands";
    internal const string CompletedItemTitle = "Buy milk";
    internal const string CompletedItemDescription = "Semi-skimmed, two litres";
    internal const string OpenItemTitle = "Collect the parcel";

    internal static readonly string[] CompletedItemTags = ["urgent", "shop"];

    /// <summary>
    /// A fully populated aggregate, as if it had just been loaded: audit values set, version set, one
    /// completed item with a description and two tags, one open item with neither.
    /// </summary>
    internal static TodoList FullyPopulated()
    {
        var completed = TodoItem.Rehydrate(
            CompletedItemId,
            ListId,
            CompletedItemTitle,
            CompletedItemDescription,
            CompletedAt,
            CompletedItemTags);

        var open = TodoItem.Rehydrate(
            OpenItemId,
            ListId,
            OpenItemTitle,
            description: null,
            completedAt: null,
            tags: []);

        var aggregate = TodoList.Rehydrate(ListId, OwnerId, Name, [completed, open]);

        ((IVersioned)aggregate).SetVersion(Version);
        ((IAuditable)aggregate).SetCreated(CreatedAt, CreatedBy);
        ((IAuditable)aggregate).SetLastModified(LastModifiedAt, LastModifiedBy);

        return aggregate;
    }

    /// <summary>
    /// The same list and the same two items, with <b>every</b> domain-owned value different from
    /// <see cref="FullyPopulated"/> — including the ones only an update can move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The item ids are deliberately unchanged. New ids would make each item a fresh insert, and an
    /// insert goes through <c>ToNewRecord</c>; a property the update path forgets would then be carried
    /// anyway and the omission would pass. Same id, different everything else, is what forces the
    /// comparison through <c>WriteTo</c>.
    /// </para>
    /// <para>
    /// The completion state is swapped rather than merely moved: the completed item is reopened and the
    /// open one completed. <c>IsCompleted</c> is derived from <c>CompletedAt</c>, and leaving both items
    /// completed would leave a value that is the same in both samples — which is exactly the comparison
    /// that proves nothing.
    /// </para>
    /// </remarks>
    internal static TodoList DifferentInEveryDomainOwnedValue()
    {
        var first = TodoItem.Rehydrate(
            CompletedItemId,
            ListId,
            OtherCompletedItemTitle,
            OtherCompletedItemDescription,
            completedAt: null,
            OtherCompletedItemTags);

        var second = TodoItem.Rehydrate(
            OpenItemId,
            ListId,
            OtherOpenItemTitle,
            OtherOpenItemDescription,
            OtherCompletedAt,
            OtherOpenItemTags);

        var aggregate = TodoList.Rehydrate(ListId, OtherOwnerId, OtherName, [first, second]);

        ((IVersioned)aggregate).SetVersion(OtherVersion);
        ((IAuditable)aggregate).SetCreated(OtherCreatedAt, OtherCreatedBy);
        ((IAuditable)aggregate).SetLastModified(OtherLastModifiedAt, OtherLastModifiedBy);

        return aggregate;
    }

    internal static Guid ListId { get; } = new("0199a3c4-1111-7000-8000-000000000001");

    internal static Guid CompletedItemId { get; } = new("aaaaaaaa-0000-0000-0000-000000000001");

    internal static Guid OpenItemId { get; } = new("aaaaaaaa-0000-0000-0000-000000000002");

    // ---- The second, entirely different set of values -------------------------------------------

    internal static readonly Guid OtherOwnerId = new("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f");
    internal static readonly Guid OtherCreatedBy = new("22222222-3333-4444-5555-666666666666");
    internal static readonly Guid OtherLastModifiedBy = new("77777777-8888-9999-aaaa-bbbbbbbbbbbb");
    internal static readonly DateTimeOffset OtherCreatedAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherLastModifiedAt = new(2025, 7, 9, 12, 13, 14, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherCompletedAt = new(2025, 7, 10, 15, 16, 17, TimeSpan.Zero);

    internal const uint OtherVersion = 123_456u;

    internal const string OtherName = "Errands, reorganised";
    internal const string OtherCompletedItemTitle = "Buy oat milk";
    internal const string OtherCompletedItemDescription = "One litre, barista edition";
    internal const string OtherOpenItemTitle = "Return the parcel";
    internal const string OtherOpenItemDescription = "Keep the original packaging";

    internal static readonly string[] OtherCompletedItemTags = ["later", "market"];
    internal static readonly string[] OtherOpenItemTags = ["post"];
}
