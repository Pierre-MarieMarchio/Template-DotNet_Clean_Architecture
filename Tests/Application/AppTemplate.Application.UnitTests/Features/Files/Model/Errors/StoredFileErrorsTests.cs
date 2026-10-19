using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Model.Errors;

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
        StoredFileErrors.DirectUploadUnavailable().Code.ShouldBe("storedFile.directUploadUnavailable");
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

    /// <summary>
    /// The deployment's choice of store, not a malformed request: no change to the body helps.
    /// </summary>
    [Fact]
    public void ADirectUploadAgainstAStoreThatCannotSign_IsAConflict() =>
        StoredFileErrors.DirectUploadUnavailable().Type.ShouldBe(ErrorType.Conflict);
}
