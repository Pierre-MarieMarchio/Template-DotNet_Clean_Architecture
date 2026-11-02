using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

public static class GetStoredFilesRequestBinder
{
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
