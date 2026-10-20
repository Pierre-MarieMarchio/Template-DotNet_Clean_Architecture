using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;

public sealed class SecurityEventCollectionPolicy : ICollectionPolicy
{
    public const string OccurredAtField = "occurredAt";

    public static readonly SecurityEventCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } = [SortableField.OffsetOnly(OccurredAtField)];

    public string DefaultSort => "occurredAt:desc";

    public int MaxSortTerms => 1;

    public int MaxPageSize => 100;

    public int DefaultPageSize => 20;
}
