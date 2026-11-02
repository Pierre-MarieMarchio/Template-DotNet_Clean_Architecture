using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.Model.Policies;

public static class StoredFileQuotaPolicy
{
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
