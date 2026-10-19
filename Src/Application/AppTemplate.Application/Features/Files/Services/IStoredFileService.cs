using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Domain.Features.Files.Model.Entities;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// The gate every file command loads its aggregate through: identity, ownership and the version precondition, in that
/// order, on the same mechanism as the other features (<c>OwnedAggregate.Require</c>).
/// </summary>
public interface IStoredFileService
{
    /// <summary>
    /// The aggregate, or <see cref="StoredFileErrors.FileNotFound"/> for an anonymous caller, an unknown id or
    /// someone else's file, or <see cref="ConcurrencyErrors.PreconditionFailed"/> once ownership is established but
    /// the version is stale.
    /// </summary>
    Task<Result<StoredFile>> LoadOwnedAsync(
        Guid storedFileId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a file whose content the caller may read: their own in any state, or an available file of a category
    /// readable by any signed-in user (a category no longer configured counts as private). Anything else is
    /// <see cref="StoredFileErrors.FileNotFound"/>, so a non-owner learns nothing.
    /// </summary>
    Task<Result<StoredFile>> LoadReadableAsync(Guid storedFileId, CancellationToken cancellationToken = default);
}
