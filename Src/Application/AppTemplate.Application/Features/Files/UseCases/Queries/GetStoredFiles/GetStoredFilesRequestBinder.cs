using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <summary>
/// Turns a <see cref="GetStoredFilesQuery"/>'s raw strings into the shape
/// <see cref="IStoredFileQueries"/> accepts: what a caller may filter by, and which sortable field
/// holds an instant. <see cref="CollectionBinding"/> does the part shared by every collection.
/// </summary>
public static class GetStoredFilesRequestBinder
{
    /// <summary>Binds one page of stored files, or answers the caller's first mistake.</summary>
    public static Result<FeaturePageRequest<StoredFileFilter>> Bind(GetStoredFilesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return CollectionBinding.Bind(
            query,
            StoredFileCollectionPolicy.Instance,
            () => StoredFileFilter.Create(query.Search, query.State),
            StoredFileCollectionPolicy.RegisteredAtField);
    }
}
