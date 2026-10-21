using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.ConfirmFileUpload;

public sealed class ConfirmFileUploadUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new ConfirmFileUploadCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_NeverAsksTheStoreAnything()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new ConfirmFileUploadCommand(Guid.CreateVersion7()), TestToken);

        await _content.DidNotReceive().DescribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherUsersFile_IsNotConfirmed()
    {
        var foreign = AStoredFile.PendingOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        foreign.State.ShouldBe(StoredFileState.Pending);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherUsersFile_IsIndistinguishableFromAMissingOne()
    {
        var foreign = AStoredFile.PendingOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var foreignResult = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(foreign.Id), TestToken);
        var missingResult = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(missingId), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    [Fact]
    public async Task AFileWithNothingDeposited_IsReportedAndLeftPending()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        _content.DescribeAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns((StoredObjectDescription?)null);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.depositMissing");
        storedFile.State.ShouldBe(StoredFileState.Pending);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADepositOfTheWrongSize_LeavesTheFilePending()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, sizeInBytes: AStoredFile.SizeInBytes + 1, checksum: AStoredFile.Checksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("domain.invariantViolated");
        storedFile.State.ShouldBe(StoredFileState.Pending);
        storedFile.AvailableAt.ShouldBeNull();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADepositWithTheWrongChecksum_LeavesTheFilePending()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.OtherChecksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        storedFile.State.ShouldBe(StoredFileState.Pending);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AZeroByteDeposit_IsRefused()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, sizeInBytes: 0, checksum: AStoredFile.Checksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        storedFile.State.ShouldBe(StoredFileState.Pending);
    }

    [Fact]
    public async Task TheStore_IsAskedAboutTheAggregatesOwnKey()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum);

        await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        await _content.Received(1).DescribeAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AChecksumReportedInUpperCase_StillMatches()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum.ToUpperInvariant());

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        storedFile.State.ShouldBe(StoredFileState.Deposited);
    }

    [Fact]
    public async Task AMatchingDeposit_RecordsTheDepositAndCommits()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        storedFile.State.ShouldBe(StoredFileState.Deposited);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMatchingDeposit_DoesNotMakeTheFileServable()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum);

        await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        storedFile.State.ShouldNotBe(StoredFileState.Available);
        storedFile.AvailableAt.ShouldBeNull();
    }

    [Fact]
    public async Task AConfirmedFile_IsProjectedBackWithoutASecondQuery()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.Value.Value.Id.ShouldBe(storedFile.Id);
        result.Value.Value.State.ShouldBe(StoredFileState.Deposited);
        result.Value.Value.SizeInBytes.ShouldBe(AStoredFile.SizeInBytes);
    }

    [Fact]
    public async Task AnAlreadyAvailableFile_IsRefused()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
    }

    private StoredFile GivenTheCallerOwnsAPendingFile()
    {
        var storedFile = AStoredFile.PendingOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private void GivenTheStoreHolds(StoredFile storedFile, long sizeInBytes, string checksum) =>
        _content.DescribeAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns(new StoredObjectDescription(sizeInBytes, checksum));

    private ConfirmFileUploadUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private ConfirmFileUploadUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new StoredFileService(_repository, currentUser, FilesSettings.Default().AsOptions()),
        new StoredFileDepositService(_content, _unitOfWork),
        new ConfirmFileUploadCommandValidator());
}
