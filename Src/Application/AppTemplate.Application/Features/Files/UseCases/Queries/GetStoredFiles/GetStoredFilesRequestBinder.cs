using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <summary>
/// Turns a <see cref="GetStoredFilesQuery"/>'s raw strings into a <see cref="StoredFilePageRequest"/>,
/// applying <see cref="StoredFileCollectionPolicy.Instance"/>'s whitelist along the way. Kept apart
/// from the use case because none of this is application logic — it is query-string translation.
/// </summary>
/// <remarks>
/// <see cref="CollectionOrder"/> does the half of that translation that is the same for every
/// collection. What is written out here is what only this feature knows: what a caller may filter
/// its files by, and which of its sortable fields holds an instant rather than a string.
/// </remarks>
public static class GetStoredFilesRequestBinder
{
    public static Result<StoredFilePageRequest> Bind(GetStoredFilesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var policy = StoredFileCollectionPolicy.Instance;

        var orderResult = CollectionOrder.Parse(query.Paging, query.Sort, policy);

        if (orderResult.IsFailure)
        {
            return orderResult.To<StoredFilePageRequest>();
        }

        var order = orderResult.Value;

        var filterResult = StoredFileFilter.Create(query.Search, query.State);

        if (filterResult.IsFailure)
        {
            return filterResult.To<StoredFilePageRequest>();
        }

        var pagingResult = order.ToPageRequest(
            query.Page,
            query.PageSize,
            query.Cursor,
            StoredFileCollectionPolicy.RegisteredAtField);

        if (pagingResult.IsFailure)
        {
            return pagingResult.To<StoredFilePageRequest>();
        }

        return StoredFilePageRequest.Of(pagingResult.Value, order.Sort, filterResult.Value);
    }
}
