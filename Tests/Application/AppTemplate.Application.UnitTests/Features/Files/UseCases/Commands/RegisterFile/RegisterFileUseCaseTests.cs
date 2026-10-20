using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.RegisterFile;

public sealed class RegisterFileUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private static readonly IssuedUploadGrant _grant = new(
        "https://store.example/upload",
        "PUT",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = AStoredFile.MediaType },
        StubDateTimeProvider.DefaultInstant.AddMinutes(30));

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly IFileDirectTransfer _transfer = Substitute.For<IFileDirectTransfer>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RecordingOwnerQuotaLock _quotaLock = new();
    private readonly FilesOptions _files = FilesSettings.Default();

    public RegisterFileUseCaseTests()
    {
        _queries.GetUsageForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, 0, 0));

        _transfer.IsSupported.Returns(true);

        _transfer.CreateUploadGrantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_grant);
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ACommand(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_MintsNoGrantAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ACommand(), TestToken);

        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transfer.DidNotReceive().CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region A store that cannot sign

    [Fact]
    public async Task AStoreThatCannotSign_RefusesRegistrationAndWritesNothing()
    {
        _transfer.IsSupported.Returns(false);

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("storedFile.directUploadUnavailable");
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _transfer.DidNotReceive().CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region The category's rules

    [Fact]
    public async Task AnUnknownCategory_IsAValidationFailureAndWritesNothing()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Category = "videos" }, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
    }

    [Fact]
    public async Task AMediaTypeTheCategoryRefuses_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(
            ACommand() with { Category = FilesSettings.Document },
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ASizeAboveTheCategoryMaximum_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(
            ACommand() with { SizeInBytes = _files.Categories[FilesSettings.Image].MaxSizeInBytes + 1 },
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ACategoryWithoutDirectUploads_RefusesRegistrationAndWritesNothing()
    {
        _files.Categories[FilesSettings.Image].UploadModes.Remove(FileCategoryOptions.DirectUploadMode);

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("storedFile.directUploadUnavailable");
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _queries.DidNotReceive().GetUsageForOwnerAsync(
            Arg.Any<UserId>(),
            Arg.Any<FileCategory>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARegisteredFile_KeepsTheCategoryItWasRegisteredUnder()
    {
        StoredFile? staged = null;
        _repository.When(repository => repository.Add(Arg.Any<StoredFile>()))
            .Do(call => staged = call.Arg<StoredFile>());

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        staged.ShouldNotBeNull();
        staged.Category.ShouldBe(FileCategory.Create(FilesSettings.Image));
    }

    #endregion

    #region Quota

    [Fact]
    public async Task TheCategorysOwnPendingLimit_Binds()
    {
        _files.Categories[FilesSettings.Image].Quota.MaxPendingUploads = 2;
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, 2, 0));

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    [Fact]
    public async Task AFullCategory_LeavesAnotherCategoryOpen()
    {
        var full = new OwnerStorageUsage(0, 0, _files.Categories[FilesSettings.Document].Quota.MaxPendingUploads, 0);
        _queries.GetUsageForOwnerAsync(_callerId, FileCategory.Create(FilesSettings.Document), Arg.Any<CancellationToken>())
            .Returns(full);

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnOwnerAtTheirPendingLimit_IsRefused()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, _files.Categories[FilesSettings.Image].Quota.MaxPendingUploads, 0));

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    [Fact]
    public async Task AnOwnerOverQuota_GetsNoUploadGrant()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, _files.Categories[FilesSettings.Image].Quota.MaxPendingUploads, 0));

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        await _transfer.DidNotReceive().CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheQuota_IsMeasuredAgainstTheCallerInTheNamedCategory()
    {
        await UseCase().ExecuteAsync(ACommand(), TestToken);

        await _queries.Received(1).GetUsageForOwnerAsync(_callerId, FileCategory.Create(FilesSettings.Image), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileThatWouldOverflowTheByteAllowance_IsRefused()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(1, _files.Categories[FilesSettings.Image].Quota.MaxBytes, 0, 0));

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankName_IsAValidationFailure(string name)
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Name = name }, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AChecksumOfTheWrongLength_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Checksum = "abc" }, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task AChecksumThatIsNotHexadecimal_IsRefusedByTheDomainAsAConflict()
    {
        var result = await UseCase().ExecuteAsync(
            ACommand() with { Checksum = new string('z', 64) },
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("domain.invariantViolated");
    }

    [Fact]
    public async Task AReservedDeviceName_IsRefusedWithoutWritingAnything()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Name = "NUL.txt" }, TestToken);

        result.IsFailure.ShouldBeTrue();
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
    }

    [Fact]
    public async Task ASizeOfZero_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { SizeInBytes = 0 }, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ANullCommand_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

    #region Success

    [Fact]
    public async Task ARegisteredFile_IsStagedAndCommitted()
    {
        await UseCase().ExecuteAsync(ACommand(), TestToken);

        _repository.Received(1).Add(Arg.Any<StoredFile>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARegisteredFile_BelongsToTheCallerAndStartsPending()
    {
        StoredFile? staged = null;
        _repository.When(repository => repository.Add(Arg.Any<StoredFile>()))
            .Do(call => staged = call.Arg<StoredFile>());

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        staged.ShouldNotBeNull();
        staged.OwnerId.ShouldBe(_callerId);
        staged.State.ShouldBe(StoredFileState.Pending);
        staged.RegisteredAt.ShouldBe(StubDateTimeProvider.DefaultInstant);
    }

    [Fact]
    public async Task TheRow_IsCommittedBeforeTheGrantIsMinted()
    {
        bool committed = false;
        bool grantedBeforeCommit = false;

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            committed = true;

            return 1;
        });

        _transfer.CreateUploadGrantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                grantedBeforeCommit = !committed;

                return _grant;
            });

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        grantedBeforeCommit.ShouldBeFalse();
    }

    [Fact]
    public async Task TheGrant_IsMintedOutsideTheQuotaLock()
    {
        bool heldWhileGranting = true;

        _transfer.CreateUploadGrantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                heldWhileGranting = _quotaLock.IsHeld;

                return _grant;
            });

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        _quotaLock.Calls.ShouldBe(1);
        heldWhileGranting.ShouldBeFalse();
    }

    [Fact]
    public async Task TheGrant_IsMintedForTheAggregatesOwnKey()
    {
        StoredFile? staged = null;
        _repository.When(repository => repository.Add(Arg.Any<StoredFile>()))
            .Do(call => staged = call.Arg<StoredFile>());

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        staged.ShouldNotBeNull();
        await _transfer.Received(1).CreateUploadGrantAsync(
            staged.ObjectKey.Value,
            AStoredFile.MediaType,
            AStoredFile.SizeInBytes,

            AStoredFile.Checksum,
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOutcome_CarriesTheIdAndTheGrant()
    {
        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.StoredFileId.ShouldNotBe(Guid.Empty);
        result.Value.Upload.ShouldBeSameAs(_grant);
    }

    [Fact]
    public async Task TheUploadWindow_IsBoundedAndShort()
    {
        await UseCase().ExecuteAsync(ACommand(), TestToken);

        await _transfer.Received(1).CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Is<TimeSpan>(lifetime => lifetime > TimeSpan.Zero && lifetime <= TimeSpan.FromHours(1)),
            Arg.Any<CancellationToken>());
    }

    #endregion

    private static RegisterFileCommand ACommand() =>
        new(FilesSettings.Image, "holiday.png", AStoredFile.MediaType, AStoredFile.SizeInBytes, AStoredFile.Checksum);

    private RegisterFileUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private RegisterFileUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new StoredFileRegistrationService(
            _repository,
            _queries,
            _quotaLock,
            _unitOfWork,
            new StubDateTimeProvider(),
            _files.AsOptions()),
        _transfer,
        currentUser,
        _files.AsOptions(),
        new RegisterFileCommandValidator(_files.AsOptions()));
}
