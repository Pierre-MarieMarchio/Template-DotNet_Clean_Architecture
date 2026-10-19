using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Features.Files.Model.Policies;

/// <summary>
/// The Files collection whitelist. Each sortable name promises a cheap ordering, so it needs a composite index
/// <c>(OwnerId, field, Id)</c>; size is absent until someone needs it and the index exists.
/// </summary>
public sealed class StoredFileCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";

    public const string RegisteredAtField = "registeredAt";

    /// <summary>
    /// Offset-only: the column is nullable, and a keyset comparison against NULL would skip the row the cursor came
    /// from.
    /// </summary>
    public const string AvailableAtField = "availableAt";

    public static readonly StoredFileCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(NameField),
        SortableField.Keyset(RegisteredAtField),
        SortableField.OffsetOnly(AvailableAtField),
    ];

    /// <summary>Newest first: a file list is read to find what was just put there.</summary>
    public string DefaultSort => "registeredAt:desc";

    public int MaxSortTerms => 3;

    public int MaxPageSize => 100;

    public int DefaultPageSize => 20;
}
