using System.Globalization;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Model.Policies;

public sealed class StoredFileQuotaPolicyTests
{
    private static readonly FileCategoryQuotaOptions _quota = new()
    {
        MaxPendingUploads = 3,
        MaxFiles = 5,
        MaxBytes = 10_000,
    };

    [Fact]
    public void TheDefaultQuota_IsTwentyPendingAThousandFilesAndTenGibibytes()
    {
        var quota = new FileCategoryQuotaOptions();

        quota.MaxPendingUploads.ShouldBe(20);
        quota.MaxFiles.ShouldBe(1_000);
        quota.MaxBytes.ShouldBe(10L * 1024 * 1024 * 1024);
        quota.Mode.ShouldBe(FileCategoryQuotaOptions.StrictMode);
    }

    [Fact]
    public void AnEmptyAllowance_AcceptsAFile() =>
        StoredFileQuotaPolicy.EnsureRoomFor(Usage(), _quota, 1_024).IsSuccess.ShouldBeTrue();

    [Fact]
    public void TheLastPendingSlot_IsStillAvailable() =>
        StoredFileQuotaPolicy
            .EnsureRoomFor(Usage(pendingCount: _quota.MaxPendingUploads - 1), _quota, 1_024)
            .IsSuccess.ShouldBeTrue();

    [Fact]
    public void TooManyPendingRegistrations_AreRefused()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(pendingCount: _quota.MaxPendingUploads), _quota,
            1_024);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void TooManyPendingRegistrations_AreRefusedEvenWhenNothingIsStored()
    {
        var usage = new OwnerStorageUsage(
            StoredCount: 0,
            StoredBytes: 0,
            PendingCount: _quota.MaxPendingUploads,
            PendingDeclaredBytes: 0);

        StoredFileQuotaPolicy.EnsureRoomFor(usage, _quota, 1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TooManyFiles_AreRefused()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(storedCount: _quota.MaxFiles), _quota,
            1_024);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    [Fact]
    public void TheFileCeiling_CountsPendingRowsToo()
    {
        var usage = new OwnerStorageUsage(
            StoredCount: _quota.MaxFiles - 1,
            StoredBytes: 0,
            PendingCount: 1,
            PendingDeclaredBytes: 0);

        StoredFileQuotaPolicy.EnsureRoomFor(usage, _quota, 1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AFileThatExactlyFills_TheByteAllowance_IsAccepted() =>
        StoredFileQuotaPolicy
            .EnsureRoomFor(Usage(storedBytes: _quota.MaxBytes - 1_024), _quota, 1_024)
            .IsSuccess.ShouldBeTrue();

    [Fact]
    public void AFileOneByteOver_TheByteAllowance_IsRefused()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(storedBytes: _quota.MaxBytes - 1_024), _quota,
            1_025);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    [Fact]
    public void BytesMerelyPromised_CountAgainstTheAllowance()
    {
        var usage = new OwnerStorageUsage(
            StoredCount: 0,
            StoredBytes: 0,
            PendingCount: 1,
            PendingDeclaredBytes: _quota.MaxBytes);

        StoredFileQuotaPolicy.EnsureRoomFor(usage, _quota, 1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TheRefusal_SpeaksOnlyOfTheCallersOwnAllowance()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(pendingCount: _quota.MaxPendingUploads), _quota,
            1_024);

        result.Error!.Message.ShouldContain(
            _quota.MaxPendingUploads.ToString(CultureInfo.InvariantCulture));
    }

    private static OwnerStorageUsage Usage(
        int storedCount = 0,
        long storedBytes = 0,
        int pendingCount = 0,
        long pendingDeclaredBytes = 0) =>
        new(storedCount, storedBytes, pendingCount, pendingDeclaredBytes);
}
