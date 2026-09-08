using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>
/// The catalogue of failures every feature's collection endpoint can produce. A feature's own errors
/// live beside the feature; these are the ones the generic parsing in this namespace raises, so a
/// code is never invented twice for the same shape of mistake.
/// </summary>
public static class CollectionErrors
{
    /// <summary>
    /// The <c>paging</c> family does not hold up: an unrecognised mode, a page below 1, or a
    /// <c>pageSize</c> outside the policy's range.
    /// </summary>
    public static Error InvalidPaging(string message) => Error.Validation("paging.invalid", message);

    /// <summary>
    /// The <c>sort</c> string names a field outside the feature's whitelist, repeats one, carries more
    /// terms than the policy allows, or spells a direction this contract does not recognise.
    /// </summary>
    public static Error InvalidSort(string message) => Error.Validation("sort.invalid", message);

    /// <summary>
    /// A value the caller filters by is refused on its own terms rather than by the shape of the query
    /// string — a search term past its length limit, say.
    /// </summary>
    public static Error InvalidFilter(string message) => Error.Validation("filter.invalid", message);

    /// <summary>
    /// A cursor cannot be trusted to resume the page it claims: it will not decode, names a field keyset
    /// paging cannot order by, or disagrees with the <c>sort</c> it arrived with.
    /// </summary>
    public static Error InvalidCursor(string message) => Error.Validation("cursor.invalid", message);
}
