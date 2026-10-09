using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Domain.Features.Files.Entities;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// Confirms a pending file's deposit from what the store really holds under its key, whatever the
/// upload mode that put the bytes there.
/// </summary>
public interface IStoredFileDepositService
{
    /// <summary>
    /// Describes the object under the file's key, moves the file to <c>Deposited</c> when it has the
    /// declared size and digest, and commits.
    /// </summary>
    /// <param name="storedFile">A loaded file.</param>
    /// <param name="cancellationToken">Cancels the description and the commit.</param>
    /// <returns>Success; <see cref="StoredFileErrors.DepositMissing"/> when the store holds nothing
    /// under the key; or the domain's refusal of a file that is not pending or not what was declared.
    /// Nothing is committed on a failure.</returns>
    Task<Result> ConfirmAsync(StoredFile storedFile, CancellationToken cancellationToken = default);
}
