namespace AppTemplate.Application.Core.Common.Collections;

public interface ICollectionQuery
{
    string? Paging { get; }

    int? Page { get; }

    int? PageSize { get; }

    string? Cursor { get; }

    string? Sort { get; }
}
