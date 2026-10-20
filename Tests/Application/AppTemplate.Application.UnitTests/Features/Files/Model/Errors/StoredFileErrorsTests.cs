using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Model.Errors;

public sealed class StoredFileErrorsTests
{
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

    [Fact]
    public void AnUnconfirmedDeposit_IsAConflict()
    {
        StoredFileErrors.DepositMissing(Guid.CreateVersion7()).Type.ShouldBe(ErrorType.Conflict);
        StoredFileErrors.FileNotAvailable(Guid.CreateVersion7()).Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void ADirectUploadAgainstAStoreThatCannotSign_IsAConflict() =>
        StoredFileErrors.DirectUploadUnavailable().Type.ShouldBe(ErrorType.Conflict);
}
