using AppTemplate.Application.Common.Collections;

namespace AppTemplate.Application.UnitTests.Common.Collections;

/// <summary>
/// A stand-in whitelist for exercising the generic parsing in <c>Common/Collections/</c> without
/// tying those tests to any real feature's policy.
/// </summary>
internal sealed class FakeCollectionPolicy : ICollectionPolicy
{
    public IReadOnlyList<SortableField> SortableFields { get; init; } =
    [
        SortableField.Keyset("name"),
        SortableField.Keyset("createdAt"),
        SortableField.OffsetOnly("lastModifiedAt"),
    ];

    public string DefaultSort { get; init; } = "createdAt:desc";

    public int MaxSortTerms { get; init; } = 3;

    public int MaxPageSize { get; init; } = 100;

    public int DefaultPageSize { get; init; } = 20;
}
