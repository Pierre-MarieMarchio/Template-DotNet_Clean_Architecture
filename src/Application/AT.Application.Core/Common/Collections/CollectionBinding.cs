using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

public static class CollectionBinding
{
    public static Result<FeaturePageRequest<TFilter>> Bind<TFilter>(
        ICollectionQuery query,
        ICollectionPolicy policy,
        Func<Result<TFilter>> createFilter,
        params string[] dateKeyFields)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(createFilter);

        var orderResult = CollectionOrder.Parse(query.Paging, query.Sort, policy);

        if (orderResult.IsFailure)
        {
            return orderResult.To<FeaturePageRequest<TFilter>>();
        }

        var order = orderResult.Value;

        var filterResult = createFilter();

        if (filterResult.IsFailure)
        {
            return filterResult.To<FeaturePageRequest<TFilter>>();
        }

        var pagingResult = order.ToPageRequest(query.Page, query.PageSize, query.Cursor, dateKeyFields);

        if (pagingResult.IsFailure)
        {
            return pagingResult.To<FeaturePageRequest<TFilter>>();
        }

        return FeaturePageRequest<TFilter>.Of(pagingResult.Value, order.Sort, filterResult.Value);
    }
}
