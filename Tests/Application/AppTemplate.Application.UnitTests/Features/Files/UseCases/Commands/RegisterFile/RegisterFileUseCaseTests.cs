using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.RegisterFile;

public sealed class RegisterFileUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private static readonly IssuedUploadGrant _grant = new(
        "https://store.example/upload",
        "PUT",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = AStoredFile.MediaType },
        StubDateTimeProvider.DefaultInstant.AddMinutes(30));

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public RegisterFileUseCaseTests()
    {
        _queries.GetUsageForOwnerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, 0, 0));

        _content.CreateUploadGrantAsync(
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

    /// <summary>
    /// An anonymous caller must not reach the store at all: a grant minted before the caller is
    /// known is a write right handed to nobody in particular.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_MintsNoGrantAndWritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ACommand(), TestToken);

        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _content.DidNotReceive().CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Quota

    [Fact]
    public async Task AnOwnerAtTheirPendingLimit_IsRefused()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, StoredFileQuotaPolicy.MaxPendingRegistrations, 0));

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
    }

    /// <summary>
    /// The point of the quota: a refused registration mints no upload URL. Without this, the check
    /// would be a message rather than a defence.
    /// </summary>
    [Fact]
    public async Task AnOwnerOverQuota_GetsNoUploadGrant()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, StoredFileQuotaPolicy.MaxPendingRegistrations, 0));

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        await _content.DidNotReceive().CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The usage is read for the caller, not for whoever the command might have named.</summary>
    [Fact]
    public async Task TheQuota_IsMeasuredAgainstTheCaller()
    {
        await UseCase().ExecuteAsync(ACommand(), TestToken);

        await _queries.Received(1).GetUsageForOwnerAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileThatWouldOverflowTheByteAllowance_IsRefused()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(1, StoredFileQuotaPolicy.MaxBytes, 0, 0));

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

    /// <summary>
    /// The right length and the wrong alphabet. The validator deliberately measures only the length,
    /// so this is what proves the value object is still consulted afterwards — and that its refusal
    /// arrives as a conflict rather than as a 500.
    /// </summary>
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

    /// <summary>
    /// A reserved Windows device name. Nothing the validator states catches it, so this is the
    /// second half of the same argument: the value objects are the rule, and the validator is only
    /// the fast, field-shaped half of it.
    /// </summary>
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

    /// <summary>
    /// The ordering that keeps the store from holding bytes nothing names: the row is committed
    /// first, and only then is a URL minted for its key. Swapping the two in the use case turns this
    /// red.
    /// </summary>
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

        _content.CreateUploadGrantAsync(
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

    /// <summary>
    /// The grant is minted for the key the aggregate reserved, never for one recomputed from the
    /// file's id — see <c>ObjectKey</c>, where that is the load-bearing decision of the feature.
    /// </summary>
    [Fact]
    public async Task TheGrant_IsMintedForTheAggregatesOwnKey()
    {
        StoredFile? staged = null;
        _repository.When(repository => repository.Add(Arg.Any<StoredFile>()))
            .Do(call => staged = call.Arg<StoredFile>());

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        staged.ShouldNotBeNull();
        await _content.Received(1).CreateUploadGrantAsync(
            staged.ObjectKey.Value,
            AStoredFile.MediaType,
            AStoredFile.SizeInBytes,

            // The digest the client declared, which the grant has to bind so the store refuses any
            // other bytes. Asserted by value rather than with Arg.Any: passing the wrong one here
            // would mint a grant no honest deposit could satisfy, and nothing else would notice.
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

    /// <summary>A write right that never expires is a write right somebody keeps.</summary>
    [Fact]
    public async Task TheUploadWindow_IsBoundedAndShort()
    {
        await UseCase().ExecuteAsync(ACommand(), TestToken);

        await _content.Received(1).CreateUploadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Is<TimeSpan>(lifetime => lifetime > TimeSpan.Zero && lifetime <= TimeSpan.FromHours(1)),
            Arg.Any<CancellationToken>());
    }

    #endregion

    private static RegisterFileCommand ACommand() =>
        new("holiday.png", AStoredFile.MediaType, AStoredFile.SizeInBytes, AStoredFile.Checksum);

    private RegisterFileUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private RegisterFileUseCase UseCaseFor(ICurrentUser currentUser) => new(
        _repository,
        _queries,
        _content,
        _unitOfWork,
        currentUser,
        new StubDateTimeProvider(),
        new RegisterFileCommandValidator());
}
