using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Policies;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// The file feature's own collection whitelist.
/// <para>
/// Three fields, and no more, because each name here is a promise that ordering by it is cheap —
/// which means a composite index <c>(OwnerId, &lt;field&gt;, Id)</c> in the persistence
/// configuration. Size is a plausible fourth and is deliberately absent: nobody has asked to sort
/// files by weight, and adding the name before the index is how a whitelist stops meaning anything.
/// </para>
/// </summary>
public sealed class StoredFileCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";

    public const string RegisteredAtField = "registeredAt";

    /// <summary>
    /// Offset-only because the column is nullable — a file that was never confirmed has no
    /// confirmation instant. A keyset comparison against <c>NULL</c> is neither true nor false, so
    /// the row a cursor was minted from would be skipped rather than resumed from.
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
