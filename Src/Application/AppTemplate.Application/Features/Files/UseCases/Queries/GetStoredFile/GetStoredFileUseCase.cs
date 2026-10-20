using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;

public sealed class GetStoredFileUseCase(
    IStoredFileQueries queries,
    ICurrentUser currentUser,
    IValidator<GetStoredFileQuery> validator) : IGetStoredFileUseCase
{
    public async Task<Result<Versioned<StoredFileDto>>> ExecuteAsync(
        GetStoredFileQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<StoredFileDto>>();
        }

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
