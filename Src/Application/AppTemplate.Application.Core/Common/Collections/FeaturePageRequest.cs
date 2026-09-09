namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>
/// The only collection shape a feature's read port accepts: the paging, the sort order and that
/// feature's own filter, all three already validated.
/// </summary>
/// <remarks>
/// Unconstructible outside this project — <see cref="CollectionBinding.Bind{TFilter}"/> is the one
/// thing that can make one, and it can only do so after <c>PageRequest.Create</c>,
/// <c>SortOrder.Parse</c> and the feature's own filter factory have each answered. So the read side
/// never re-validates what already cleared the whitelist, and there is no second way in.
/// </remarks>
/// <typeparam name="TFilter">The feature's own filter type.</typeparam>
public sealed record FeaturePageRequest<TFilter>
{
    private FeaturePageRequest(PageRequest paging, SortOrder sort, TFilter filter)
    {
        Paging = paging;
        Sort = sort;
        Filter = filter;
    }

    /// <summary>The page or the cursor the caller resolved to.</summary>
    public PageRequest Paging { get; }

    /// <summary>Never empty: a blank <c>sort</c> parsed the feature policy's own default.</summary>
    public SortOrder Sort { get; }

    /// <summary>What this feature lets a caller narrow by.</summary>
    public TFilter Filter { get; }

    internal static FeaturePageRequest<TFilter> Of(PageRequest paging, SortOrder sort, TFilter filter) =>
        new(paging, sort, filter);
}
