using AppTemplate.Application.Core.Common.Collections;

namespace AppTemplate.Application.Core.Common.Policies;

public interface ICollectionPolicy
{
    IReadOnlyList<SortableField> SortableFields { get; }

    string DefaultSort { get; }

    int MaxSortTerms { get; }

    int MaxPageSize { get; }

    int DefaultPageSize { get; }
}
