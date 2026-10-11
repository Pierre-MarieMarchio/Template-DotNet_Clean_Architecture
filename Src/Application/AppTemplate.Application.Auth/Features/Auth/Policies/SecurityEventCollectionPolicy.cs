using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Auth.Features.Auth.Policies;

/// <summary>
/// The page sizes a security-event listing accepts. One order only — newest first — and offset
/// paging only: a trail is read from the top, and nothing about it needs a caller-chosen sort.
/// </summary>
public sealed class SecurityEventCollectionPolicy : ICollectionPolicy
{
    /// <summary>The one field a listing is ordered by.</summary>
    public const string OccurredAtField = "occurredAt";

    /// <summary>The shared instance; the policy has no state.</summary>
    public static readonly SecurityEventCollectionPolicy Instance = new();

    /// <inheritdoc />
    public IReadOnlyList<SortableField> SortableFields { get; } = [SortableField.OffsetOnly(OccurredAtField)];

    /// <inheritdoc />
    public string DefaultSort => "occurredAt:desc";

    /// <inheritdoc />
    public int MaxSortTerms => 1;

    /// <inheritdoc />
    public int MaxPageSize => 100;

    /// <inheritdoc />
    public int DefaultPageSize => 20;
}
