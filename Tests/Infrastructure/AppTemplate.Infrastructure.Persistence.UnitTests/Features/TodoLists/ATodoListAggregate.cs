using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

internal static class ATodoListAggregate
{
    internal static readonly UserId OwnerId = UserId.Create(new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60"));
    internal static readonly Guid CreatedBy = new("11111111-2222-3333-4444-555555555555");
    internal static readonly Guid LastModifiedBy = new("66666666-7777-8888-9999-aaaaaaaaaaaa");
    internal static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    internal static readonly DateTimeOffset LastModifiedAt = new(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
    internal static readonly DateTimeOffset CompletedAt = new(2026, 3, 6, 11, 12, 13, TimeSpan.Zero);

    internal const uint Version = 987_654u;

    internal const string Name = "Groceries and other errands";
    internal const string CompletedItemTitle = "Buy milk";
    internal const string CompletedItemDescription = "Semi-skimmed, two litres";
    internal const string OpenItemTitle = "Collect the parcel";

    internal static readonly string[] CompletedItemTags = ["urgent", "shop"];

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

    internal static readonly UserId OtherOwnerId = UserId.Create(new("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f"));
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
