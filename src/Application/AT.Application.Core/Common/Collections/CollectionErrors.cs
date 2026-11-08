using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

public static class CollectionErrors
{
    public static Error InvalidPaging(string message) => Error.Validation("paging.invalid", message);

    public static Error InvalidSort(string message) => Error.Validation("sort.invalid", message);

    public static Error InvalidFilter(string message) => Error.Validation("filter.invalid", message);

    public static Error InvalidCursor(string message) => Error.Validation("cursor.invalid", message);
}
