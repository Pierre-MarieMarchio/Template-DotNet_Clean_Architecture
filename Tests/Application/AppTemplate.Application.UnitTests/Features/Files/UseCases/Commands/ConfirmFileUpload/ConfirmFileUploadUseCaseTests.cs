using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.ConfirmFileUpload;

public sealed class ConfirmFileUploadUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication and ownership

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

    /// <summary>
    /// Confirming somebody else's upload would make their file servable on this caller's say-so, and
    /// would tell them the file exists into the bargain.
    /// </summary>
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

    #endregion

    #region The deposit

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

    /// <summary>
    /// The check the whole two-step flow exists for. A deposit whose length does not match what was
    /// declared leaves the file pending — nothing is served, and the abandonment sweep is what
    /// eventually clears it. Deleting the size comparison in <c>StoredFile.ConfirmDeposit</c>
    /// turns this red.
    /// </summary>
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

    /// <summary>
    /// The same, for the digest — which is the half that catches content substituted for content of
    /// exactly the same length.
    /// </summary>
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

    /// <summary>
    /// A zero-byte object is what an interrupted deposit leaves behind, and it is indistinguishable
    /// from no deposit at all as far as the store is concerned. <c>FileSize</c> refuses it, so it
    /// cannot be confirmed as a real file.
    /// </summary>
    [Fact]
    public async Task AZeroByteDeposit_IsRefused()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, sizeInBytes: 0, checksum: AStoredFile.Checksum);

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        storedFile.State.ShouldBe(StoredFileState.Pending);
    }

    /// <summary>
    /// The observed values come from the store, and the store is asked about the key the aggregate
    /// holds — not one recomputed from anything the caller sent.
    /// </summary>
    [Fact]
    public async Task TheStore_IsAskedAboutTheAggregatesOwnKey()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum);

        await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        await _content.Received(1).DescribeAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>());
    }

    /// <summary>Casing is normalised on both sides, so two tools disagreeing about it is not a
    /// mismatch.</summary>
    [Fact]
    public async Task AChecksumReportedInUpperCase_StillMatches()
    {
        var storedFile = GivenTheCallerOwnsAPendingFile();
        GivenTheStoreHolds(storedFile, AStoredFile.SizeInBytes, AStoredFile.Checksum.ToUpperInvariant());

        var result = await UseCase().ExecuteAsync(new ConfirmFileUploadCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        storedFile.State.ShouldBe(StoredFileState.Deposited);
    }

    #endregion

    #region Success

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

    /// <summary>
    /// <b>The guarantee this endpoint does not give.</b> A matching deposit is not a readable file:
    /// nothing has read the content, so the file is not servable and carries no availability instant.
    /// Making this endpoint release the file again — because it is the convenient place, and because
    /// a client is already waiting there — would serve every byte anyone uploads without one of them
    /// having been looked at.
    /// </summary>
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

    /// <summary>Confirming twice is refused by the aggregate: only a pending file can have its
    /// deposit confirmed.</summary>
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

    [Fact]
    public async Task ANullCommand_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

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
        new StoredFileService(_repository, currentUser),
        _content,
        _unitOfWork,
        new ConfirmFileUploadCommandValidator());
}
