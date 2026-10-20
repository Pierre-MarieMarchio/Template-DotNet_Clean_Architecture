using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Features.TodoLists.Model.Policies;

public sealed class TodoListCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";

    public const string CreatedAtField = "createdAt";

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
