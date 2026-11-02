using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Features.Files.Model.Policies;

public sealed class StoredFileCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";

    public const string RegisteredAtField = "registeredAt";

    public const string AvailableAtField = "availableAt";

    public static readonly StoredFileCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(NameField),
        SortableField.Keyset(RegisteredAtField),
        SortableField.OffsetOnly(AvailableAtField),
    ];

    public string DefaultSort => "registeredAt:desc";

    public int MaxSortTerms => 3;

    public int MaxPageSize => 100;

    public int DefaultPageSize => 20;
}
