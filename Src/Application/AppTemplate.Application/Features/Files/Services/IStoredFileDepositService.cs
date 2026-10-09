using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Domain.Features.Files.Entities;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// Confirms a pending file's deposit from what the store really holds under its key, whatever the upload mode.
/// </summary>
public interface IStoredFileDepositService
{
    /// <summary>
    /// Describes the object under the file's key, moves the file to deposited when it has the declared size and
    /// digest, and commits.
    /// </summary>
    /// <returns>Success; <see cref="StoredFileErrors.DepositMissing"/> when the store holds nothing; or the domain's
    /// refusal. Nothing is committed on a failure.</returns>
    Task<Result> ConfirmAsync(StoredFile storedFile, CancellationToken cancellationToken = default);
}
