using AppTemplate.Application.Common.Collections;

namespace AppTemplate.Application.Features.TodoLists.Policies;

/// <summary>The to-do list feature's own collection whitelist.</summary>
public sealed class TodoListCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";

    public const string CreatedAtField = "createdAt";

    /// <summary>
    /// Offset-only because the column is nullable: a keyset comparison against a row whose key is
    /// <c>NULL</c> is neither true nor false, so the row the cursor was minted from would be skipped
    /// rather than resumed from.
    /// </summary>
    public const string LastModifiedAtField = "lastModifiedAt";

    public static readonly TodoListCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(NameField),
        SortableField.Keyset(CreatedAtField),
        SortableField.OffsetOnly(LastModifiedAtField),
    ];

    public string DefaultSort => "createdAt:desc";

    public int MaxSortTerms => 3;

    public int MaxPageSize => 100;

    public int DefaultPageSize => 20;
}
