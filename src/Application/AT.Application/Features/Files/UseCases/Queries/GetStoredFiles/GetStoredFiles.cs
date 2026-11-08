using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using Mediator;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

public sealed record GetStoredFilesQuery(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? State) : ICollectionQuery, IQuery<Result<PagedResult<StoredFileDto>>>
{
    public static GetStoredFilesQuery Offset(int? page, int? pageSize) =>
        new(null, page, pageSize, null, null, null, null);
}

public sealed class GetStoredFilesQueryHandler(
    IStoredFileQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetStoredFilesQuery, Result<PagedResult<StoredFileDto>>>
{
    public async ValueTask<Result<PagedResult<StoredFileDto>>> Handle(
        GetStoredFilesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<PagedResult<StoredFileDto>>();
        }

        var bound = GetStoredFilesRequestBinder.Bind(query);

        if (bound.IsFailure)
        {
            return bound.To<PagedResult<StoredFileDto>>();
        }

        return await queries.GetForOwnerAsync(userId.Value, bound.Value, cancellationToken);
    }
}
