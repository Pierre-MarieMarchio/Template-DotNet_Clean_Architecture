using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Services;

/// <summary>
/// The one path a new file row takes, whatever the upload mode: quota check, aggregate and commit, under the owner's
/// quota lock when the category's quota is <see cref="FileCategoryQuotaOptions.StrictMode"/>.
/// </summary>
public interface IStoredFileRegistrationService
{
    /// <summary>
    /// Checks the owner's quota in <paramref name="category"/>, registers the file and commits. Nothing is written on
    /// any failure.
    /// </summary>
    /// <param name="sizeInBytes">The declared size, counted against the byte allowance.</param>
    /// <returns>The committed pending aggregate; <see cref="StoredFileErrors.QuotaExceeded"/>;
    /// <see cref="StoredFileErrors.QuotaBusy"/> when a strict category's lock timed out; or the domain's refusal of a
    /// value.</returns>
    Task<Result<StoredFile>> RegisterAsync(
        UserId owner,
        FileCategory category,
        string name,
        string mediaType,
        long sizeInBytes,
        string checksum,
        CancellationToken cancellationToken = default);
}
