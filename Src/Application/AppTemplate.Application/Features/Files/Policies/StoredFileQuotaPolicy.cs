using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// What one owner may hold in one category and how many deposits they may have in flight; without it, registering is
/// a free way to mint signed upload URLs.
/// </summary>
public static class StoredFileQuotaPolicy
{
    /// <summary>
    /// Whether one more file of <paramref name="declaredSizeInBytes"/> fits in <paramref name="quota"/>, given the
    /// owner's <paramref name="usage"/>. Promised bytes count as stored. A check, not a reservation: it holds under
    /// concurrency only under the owner's quota lock (<see cref="FileCategoryQuotaOptions.StrictMode"/>).
    /// </summary>
    public static Result EnsureRoomFor(
        OwnerStorageUsage usage,
        FileCategoryQuotaOptions quota,
        long declaredSizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(quota);

        if (usage.PendingCount >= quota.MaxPendingUploads)
        {
            return Result.Failure(StoredFileErrors.QuotaExceeded(
                $"You already have {quota.MaxPendingUploads} uploads in this category waiting to be " +
                "completed. Finish or abandon one before starting another."));
        }

        if (usage.TotalCount >= quota.MaxFiles)
        {
            return Result.Failure(StoredFileErrors.QuotaExceeded(
                $"You have reached the limit of {quota.MaxFiles} files in this category. Delete one " +
                "before adding another."));
        }

        if (usage.CommittedBytes + declaredSizeInBytes > quota.MaxBytes)
        {
            return Result.Failure(StoredFileErrors.QuotaExceeded(
                $"This file would take you past your {quota.MaxBytes} byte allowance in this category."));
        }

        return Result.Success();
    }
}
