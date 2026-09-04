using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

public sealed class GetStoredFilesUseCase(
    IStoredFileQueries queries,
    ICurrentUser currentUser) : IGetStoredFilesUseCase
{
    public async Task<Result<PagedResult<StoredFileDto>>> ExecuteAsync(
        GetStoredFilesQuery query,
        CancellationToken cancellationToken = default)
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
