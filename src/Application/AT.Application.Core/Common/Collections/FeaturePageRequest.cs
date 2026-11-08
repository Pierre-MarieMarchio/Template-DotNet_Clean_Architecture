namespace AppTemplate.Application.Core.Common.Collections;

public sealed record FeaturePageRequest<TFilter>
{
    private FeaturePageRequest(PageRequest paging, SortOrder sort, TFilter filter)
    {
        Paging = paging;
        Sort = sort;
        Filter = filter;
    }

    public PageRequest Paging { get; }

    public SortOrder Sort { get; }

    public TFilter Filter { get; }

    internal static FeaturePageRequest<TFilter> Of(PageRequest paging, SortOrder sort, TFilter filter) =>
        new(paging, sort, filter);
}
