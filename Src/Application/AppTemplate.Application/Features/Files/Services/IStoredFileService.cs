using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Domain.Features.Files.Entities;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// The one gate every file command loads its aggregate through, on the same model as
/// <c>ITodoListService</c> and <c>IReminderService</c>: identity, ownership and the version
/// precondition, in that order.
/// <para>
/// The three share the mechanism and not the policy: <c>OwnedAggregate.Require</c> checks
/// ownership and the precondition, and each feature hands it the error its own callers see.
/// </para>
/// </summary>
public interface IStoredFileService
{
    /// <returns>
    /// The aggregate, or a failure — <see cref="StoredFileErrors.FileNotFound"/> for an anonymous
    /// caller, an unknown id or somebody else's file, and
    /// <see cref="ConcurrencyErrors.PreconditionFailed"/> once ownership is established but the
    /// caller named a version the aggregate no longer holds.
    /// </returns>
    Task<Result<StoredFile>> LoadOwnedAsync(
        Guid storedFileId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}
