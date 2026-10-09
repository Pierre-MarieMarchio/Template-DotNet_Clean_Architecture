using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Features.Notes.Policies;

/// <summary>The notes feature's own collection whitelist: both fields are non-null and indexed.</summary>
public sealed class NoteCollectionPolicy : ICollectionPolicy
{
    public const string TitleField = "title";

    public const string CreatedAtField = "createdAt";

    public static readonly NoteCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(TitleField),
        SortableField.Keyset(CreatedAtField),
    ];

    public string DefaultSort => "createdAt:desc";

    public int MaxSortTerms => 2;

    public int MaxPageSize => 100;

    public int DefaultPageSize => 20;
}
