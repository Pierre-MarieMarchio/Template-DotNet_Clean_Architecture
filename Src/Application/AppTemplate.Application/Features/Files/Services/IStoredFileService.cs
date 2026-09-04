using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Domain.Features.Files.Entities;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// The one gate every file command loads its aggregate through, on the same model as
/// <c>ITodoListService</c> and <c>IReminderService</c>: identity, ownership and the version
/// precondition, in that order.
/// <para>
/// The third instance of the pattern rather than a generalisation of the first two. What the three
/// share is a shape, not a rule: each names its own aggregate, its own repository and its own
/// not-found error, and a generic gate would have to be handed all three — at which point the
/// caller is writing the gate again with extra ceremony. See <c>CONTRIBUTING.md</c> on extracting
/// only what two real cases prove identical.
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
