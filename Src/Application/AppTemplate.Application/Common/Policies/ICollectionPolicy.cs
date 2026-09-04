using AppTemplate.Application.Common.Collections;

namespace AppTemplate.Application.Common.Policies;

/// <summary>
/// What a feature declares about its own collection endpoint: which fields may be sorted on, how it
/// is ordered when nobody asks, and the ceilings on what a caller may request.
/// <para>
/// The contract is here; every implementation is a <c>…CollectionPolicy</c> under that feature's own
/// <c>Features/&lt;F&gt;/Policies/</c>, because only the feature knows which of its columns are
/// indexed and cheap to order by. <c>Common/Collections/</c> owns the parsing and the enforcement,
/// which are the same for every feature, and this interface is the one thing those mechanics ask a
/// feature to declare — so it sits with the other policies rather than among them.
/// </para>
/// </summary>
/// <remarks>
/// Implementations are plain classes with a parameterless constructor, so an architecture rule can
/// discover every one of them and check its declarations without a container.
/// </remarks>
public interface ICollectionPolicy
{
    /// <summary>
    /// Every field a caller may name in <c>sort</c>. Anything else is refused with
    /// <c>sort.invalid</c> before a query is built, so no caller string reaches an expression.
    /// </summary>
    IReadOnlyList<SortableField> SortableFields { get; }

    /// <summary>
    /// The order applied when the caller sends none, in the same syntax a caller would send — so a
    /// feature's own default is parsed and whitelisted by exactly the code path caller input takes,
    /// and a typo in it fails a test rather than shipping.
    /// </summary>
    string DefaultSort { get; }

    /// <summary>
    /// How many terms <c>sort</c> may carry. Each term is an extra <c>ORDER BY</c> column, and past
    /// the first two or three they stop changing the result and start costing a sort.
    /// </summary>
    int MaxSortTerms { get; }

    /// <summary>The largest page a caller may ask for. Rows differ in weight, so features differ.</summary>
    int MaxPageSize { get; }

    /// <summary>The page size used when the caller sends none.</summary>
    int DefaultPageSize { get; }
}
