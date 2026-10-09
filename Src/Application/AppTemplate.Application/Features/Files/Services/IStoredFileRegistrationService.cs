using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// The one path a new file row takes, whatever the upload mode: the category's quota for the owner,
/// the aggregate, and the commit. Under the owner's quota lock when the category's quota is
/// <see cref="FileCategoryQuotaOptions.StrictMode"/>.
/// </summary>
public interface IStoredFileRegistrationService
{
    /// <summary>
    /// Checks the quota of <paramref name="category"/> for <paramref name="owner"/>, registers the
    /// file and commits it. Nothing is written on any failure.
    /// </summary>
    /// <param name="owner">The caller the file is registered for.</param>
    /// <param name="category">A category declared in configuration.</param>
    /// <param name="name">The file name the caller declares.</param>
    /// <param name="mediaType">The media type the caller declares.</param>
    /// <param name="sizeInBytes">The size the caller declares, counted against the byte allowance.</param>
    /// <param name="checksum">The SHA-256 the caller declares, in hexadecimal.</param>
    /// <param name="cancellationToken">Cancels the wait for the lock and the work under it.</param>
    /// <returns>The committed, pending aggregate; <see cref="StoredFileErrors.QuotaExceeded"/>;
    /// <see cref="StoredFileErrors.QuotaBusy"/> when a strict category's lock could not be obtained
    /// in time; or the domain's refusal of a value.</returns>
    Task<Result<StoredFile>> RegisterAsync(
        UserId owner,
        FileCategory category,
        string name,
        string mediaType,
        long sizeInBytes,
        string checksum,
        CancellationToken cancellationToken = default);
}
