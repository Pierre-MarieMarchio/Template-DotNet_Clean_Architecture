namespace AppTemplate.Application.Common.Collections;

/// <summary>
/// The catalogue of failures every feature's collection endpoint can produce. A feature's own errors
/// live beside the feature; these are the ones the generic parsing in this namespace raises, so a
/// code is never invented twice for the same shape of mistake.
/// </summary>
public static class CollectionErrors
{
    public static Error InvalidPaging(string message) => Error.Validation("paging.invalid", message);

    public static Error InvalidSort(string message) => Error.Validation("sort.invalid", message);

    public static Error InvalidFilter(string message) => Error.Validation("filter.invalid", message);

    public static Error InvalidCursor(string message) => Error.Validation("cursor.invalid", message);
}
