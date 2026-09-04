using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Errors;

public sealed class StoredFileErrorsTests
{
    /// <summary>
    /// Codes are the part of an error a client branches on, so they are a contract. Renaming one is
    /// a breaking change, and this is where that shows up.
    /// </summary>
    [Fact]
    public void EveryCode_IsNamespacedToTheFeature()
    {
        StoredFileErrors.FileNotFound(Guid.CreateVersion7()).Code.ShouldBe("storedFile.notFound");
        StoredFileErrors.DepositMissing(Guid.CreateVersion7()).Code.ShouldBe("storedFile.depositMissing");
        StoredFileErrors.FileNotAvailable(Guid.CreateVersion7()).Code.ShouldBe("storedFile.notAvailable");
        StoredFileErrors.QuotaExceeded("full").Code.ShouldBe("storedFile.quotaExceeded");
    }

    [Fact]
    public void AMissingFile_IsANotFoundRatherThanAForbidden() =>
        StoredFileErrors.FileNotFound(Guid.CreateVersion7()).Type.ShouldBe(ErrorType.NotFound);

    /// <summary>
    /// A state that prevents the operation, not a malformed request: the caller sent nothing wrong,
    /// the file simply has no bytes yet.
    /// </summary>
    [Fact]
    public void AnUnconfirmedDeposit_IsAConflict()
    {
        StoredFileErrors.DepositMissing(Guid.CreateVersion7()).Type.ShouldBe(ErrorType.Conflict);
        StoredFileErrors.FileNotAvailable(Guid.CreateVersion7()).Type.ShouldBe(ErrorType.Conflict);
    }
}
