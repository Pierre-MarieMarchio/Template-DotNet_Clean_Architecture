using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using Mediator;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;

public sealed record GetStoredFileQuery(Guid StoredFileId) : IQuery<Result<Versioned<StoredFileDto>>>;

public sealed class GetStoredFileQueryHandler(
    IStoredFileQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetStoredFileQuery, Result<Versioned<StoredFileDto>>>
{
    public async ValueTask<Result<Versioned<StoredFileDto>>> Handle(
        GetStoredFileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<StoredFileDto>>();
        }

        var storedFile = await queries.GetDetailAsync(query.StoredFileId, userId.Value, cancellationToken);

        if (storedFile is null)
        {
            return Result.Failure<Versioned<StoredFileDto>>(
                StoredFileErrors.FileNotFound(query.StoredFileId));
        }

        return storedFile;
    }
}
