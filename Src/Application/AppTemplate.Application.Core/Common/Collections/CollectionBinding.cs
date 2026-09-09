using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>
/// Turns a caller's raw collection query into the shape a read port accepts, applying a feature's
/// own whitelist along the way.
/// </summary>
public static class CollectionBinding
{
    /// <summary>Binds one collection request, in the order a caller's mistakes must be reported in.</summary>
    /// <remarks>
    /// The order is the contract, not an implementation detail: the sort and the paging mode are
    /// parsed first, the feature's filter is built second, and the cursor is resolved last against
    /// the order it was minted under — which is why <see cref="CollectionOrder"/> is two steps with
    /// the feature's own work between them. A caller sending two mistakes at once is told about the
    /// first of them in that order.
    /// </remarks>
    /// <typeparam name="TFilter">The feature's own filter type.</typeparam>
    /// <param name="query">The raw values, straight off the query string.</param>
    /// <param name="policy">The feature's whitelist and its ceilings.</param>
    /// <param name="createFilter">
    /// Builds the feature's filter from its own parameters. Called only once the order has parsed,
    /// so what it refuses is reported after what the order refuses.
    /// </param>
    /// <param name="dateKeyFields">
    /// This feature's sortable fields whose values are instants, for cursor-key validation.
    /// </param>
    /// <returns>The bound request, or the first refusal encountered.</returns>
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
