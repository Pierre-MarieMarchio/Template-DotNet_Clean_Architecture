using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;

/// <summary>
/// Reads one file's metadata through the read port rather than through
/// <c>IStoredFileService</c>, because there is no aggregate to load: the projection answers with the
/// version alongside the representation, from the same query, so the validator a caller is handed
/// describes the body it came with.
/// <para>
/// The owner is passed to the port and the port answers <c>null</c> for a file that is not this
/// caller's, exactly as it does for one that does not exist. The refusal is built into the query
/// rather than applied after it, which is what stops a future edit from reading the row first and
/// comparing afterwards.
/// </para>
/// </summary>
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
