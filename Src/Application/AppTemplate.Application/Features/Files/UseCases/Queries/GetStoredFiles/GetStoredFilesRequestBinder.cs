using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <summary>
/// Turns a <see cref="GetStoredFilesQuery"/>'s raw strings into the shape
/// <see cref="IStoredFileQueries"/> accepts. Kept apart from the use case because none of this is
/// application logic — it is query-string translation.
/// </summary>
/// <remarks>
/// <see cref="CollectionBinding"/> does the half of that translation that is the same for every
/// collection. What is written out here is what only this feature knows: what a caller may filter
/// its files by, and which of its sortable fields holds an instant rather than a string.
/// </remarks>
public static class GetStoredFilesRequestBinder
{
    /// <summary>Binds one page of stored files, or answers the caller's first mistake.</summary>
    /// <param name="query">The raw query values.</param>
    /// <returns>The bound request, or the refusal.</returns>
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
