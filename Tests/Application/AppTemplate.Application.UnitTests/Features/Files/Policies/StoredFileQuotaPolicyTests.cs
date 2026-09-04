using System.Globalization;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Policies;

/// <summary>
/// The three bounds, each proven to bind on its own. Without this policy, registering a file is a
/// free way to mint signed upload URLs, so every one of these is a security assertion rather than a
/// usability one.
/// </summary>
public sealed class StoredFileQuotaPolicyTests
{
    [Fact]
    public void AnEmptyAllowance_AcceptsAFile() =>
        StoredFileQuotaPolicy.EnsureRoomFor(Usage(), 1_024).IsSuccess.ShouldBeTrue();

    [Fact]
    public void TheLastPendingSlot_IsStillAvailable() =>
        StoredFileQuotaPolicy
            .EnsureRoomFor(Usage(pendingCount: StoredFileQuotaPolicy.MaxPendingRegistrations - 1), 1_024)
            .IsSuccess.ShouldBeTrue();

    [Fact]
    public void TooManyPendingRegistrations_AreRefused()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(pendingCount: StoredFileQuotaPolicy.MaxPendingRegistrations),
            1_024);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    /// <summary>
    /// The pending bound is about outstanding write rights, not about storage, so it binds even
    /// though those registrations weigh nothing at all.
    /// </summary>
    [Fact]
    public void TooManyPendingRegistrations_AreRefusedEvenWhenNothingIsStored()
    {
        var usage = new OwnerStorageUsage(
            StoredCount: 0,
            StoredBytes: 0,
            PendingCount: StoredFileQuotaPolicy.MaxPendingRegistrations,
            PendingDeclaredBytes: 0);

        StoredFileQuotaPolicy.EnsureRoomFor(usage, 1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TooManyFiles_AreRefused()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(storedCount: StoredFileQuotaPolicy.MaxFiles),
            1_024);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    /// <summary>The file ceiling counts both states: a thousand pending rows is a thousand rows.</summary>
    [Fact]
    public void TheFileCeiling_CountsPendingRowsToo()
    {
        var usage = new OwnerStorageUsage(
            StoredCount: StoredFileQuotaPolicy.MaxFiles - 1,
            StoredBytes: 0,
            PendingCount: 1,
            PendingDeclaredBytes: 0);

        StoredFileQuotaPolicy.EnsureRoomFor(usage, 1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void AFileThatExactlyFills_TheByteAllowance_IsAccepted() =>
        StoredFileQuotaPolicy
            .EnsureRoomFor(Usage(storedBytes: StoredFileQuotaPolicy.MaxBytes - 1_024), 1_024)
            .IsSuccess.ShouldBeTrue();

    [Fact]
    public void AFileOneByteOver_TheByteAllowance_IsRefused()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(storedBytes: StoredFileQuotaPolicy.MaxBytes - 1_024),
            1_025);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    /// <summary>
    /// The one that stops the quota being walked past. Counting only confirmed bytes would let a
    /// caller register everything first — each registration weighing nothing yet — and deposit
    /// afterwards, at which point no check is left to run.
    /// </summary>
    [Fact]
    public void BytesMerelyPromised_CountAgainstTheAllowance()
    {
        var usage = new OwnerStorageUsage(
            StoredCount: 0,
            StoredBytes: 0,
            PendingCount: 1,
            PendingDeclaredBytes: StoredFileQuotaPolicy.MaxBytes);

        StoredFileQuotaPolicy.EnsureRoomFor(usage, 1).IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// The message describes the caller's own allowance. It must not carry anything about the store
    /// itself or about anyone else, because it is returned verbatim.
    /// </summary>
    [Fact]
    public void TheRefusal_SpeaksOnlyOfTheCallersOwnAllowance()
    {
        var result = StoredFileQuotaPolicy.EnsureRoomFor(
            Usage(pendingCount: StoredFileQuotaPolicy.MaxPendingRegistrations),
            1_024);

        result.Error!.Message.ShouldContain(
            StoredFileQuotaPolicy.MaxPendingRegistrations.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ANullUsage_IsARejectedArgumentRatherThanAFailedResult() =>
        Should.Throw<ArgumentNullException>(() => StoredFileQuotaPolicy.EnsureRoomFor(null!, 1));

    private static OwnerStorageUsage Usage(
        int storedCount = 0,
        long storedBytes = 0,
        int pendingCount = 0,
        long pendingDeclaredBytes = 0) =>
        new(storedCount, storedBytes, pendingCount, pendingDeclaredBytes);
}
