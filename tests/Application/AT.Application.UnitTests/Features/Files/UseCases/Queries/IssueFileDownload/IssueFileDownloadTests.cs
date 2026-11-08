using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.IssueFileDownload;

public sealed class IssueFileDownloadTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private static readonly IssuedDownloadGrant _grant = new(
        "https://store.example/download",
        TheClock.DefaultInstant.AddMinutes(5));

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileDirectTransfer _transfer = Substitute.For<IFileDirectTransfer>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();

    public IssueFileDownloadTests()
    {
        _transfer.IsSupported.Returns(true);
        _transfer.CreateDownloadGrantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_grant);
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .Handle(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_GetsNoGrant()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .Handle(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AFileWhoseDepositWasNeverConfirmed_YieldsNoGrant()
    {
        var pending = AStoredFile.PendingOwnedBy(_callerId);
        GivenTheCallerCanRead(pending);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(pending.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AFileWaitingForAVerdictOnItsContent_YieldsNoGrant()
    {
        var deposited = AStoredFile.DepositedOwnedBy(_callerId);
        GivenTheCallerCanRead(deposited);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(deposited.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AQuarantinedFile_CanNeverBeServed()
    {
        var quarantined = AStoredFile.QuarantinedOwnedBy(_callerId);
        GivenTheCallerCanRead(quarantined);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(quarantined.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AQuarantinedFile_IsDistinguishableFromOneStillWaiting()
    {
        var quarantined = AStoredFile.QuarantinedOwnedBy(_callerId);
        var deposited = AStoredFile.DepositedOwnedBy(_callerId);
        GivenTheCallerCanRead(quarantined);
        GivenTheCallerCanRead(deposited);

        var refused = await UseCase().Handle(new IssueFileDownloadQuery(quarantined.Id), TestToken);
        var waiting = await UseCase().Handle(new IssueFileDownloadQuery(deposited.Id), TestToken);

        refused.Error!.Code.ShouldBe("storedFile.quarantined");
        refused.Error.Code.ShouldNotBe(waiting.Error!.Code);
        refused.Error.Message.ShouldNotContain("virus", Case.Insensitive);
        refused.Error.Message.ShouldNotContain("svg", Case.Insensitive);
    }

    [Fact]
    public async Task TheCallersOwnAvailableFile_YieldsAGrant()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        var result = await UseCase().Handle(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Redirect.ShouldBeSameAs(_grant);
        result.Value.Content.ShouldBeNull();
    }

    [Fact]
    public async Task AStoreThatCannotSign_YieldsTheContentItself()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();
        _transfer.IsSupported.Returns(false);
        using var bytes = new MemoryStream([1, 2, 3]);
        _content.OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>()).Returns(bytes);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Redirect.ShouldBeNull();
        result.Value.Content.ShouldBeSameAs(bytes);
        result.Value.FileName.ShouldBe(storedFile.Name.Value);
        result.Value.MediaType.ShouldBe(storedFile.DeclaredMediaType.Value);
        result.Value.SizeInBytes.ShouldBe(storedFile.Size.Bytes);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AStoreThatCannotSign_OpensNothingForAFileTheCallerCannotRead()
    {
        _transfer.IsSupported.Returns(false);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        await _content.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAvailableFileWithNoBytes_IsAnsweredAsNotAvailable()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();
        _transfer.IsSupported.Returns(false);
        _content.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task AnotherUsersAvailableFileInAnAuthenticatedCategory_YieldsAGrant()
    {
        var shared = AStoredFile.AvailableOwnedBy(AnotherUser(), category: FilesSettings.Image);
        GivenTheCallerCanRead(shared);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(shared.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Redirect.ShouldBeSameAs(_grant);
    }

    [Fact]
    public async Task TheOwnersFileInACategoryNoLongerConfigured_IsStillServed()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId, category: "retired");
        GivenTheCallerCanRead(storedFile);

        var result = await UseCase().Handle(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TheGrant_NamesTheAggregatesKeyAndOffersTheUsersLabel()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        await UseCase().Handle(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        await _transfer.Received(1).CreateDownloadGrantAsync(
            storedFile.ObjectKey.Value,
            storedFile.Name.Value,
            storedFile.DeclaredMediaType.Value,
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheDownloadWindow_IsMeasuredInMinutes()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        await UseCase().Handle(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        await _transfer.Received(1).CreateDownloadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<TimeSpan>(lifetime => lifetime > TimeSpan.Zero && lifetime <= TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyId_IsAValidationFailure()
    {
        var result = await UseCase().Handle(new IssueFileDownloadQuery(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    private async Task NoGrantWasMinted() =>
        await _transfer.DidNotReceive().CreateDownloadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());

    private StoredFile GivenTheCallerOwnsAnAvailableFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        GivenTheCallerCanRead(storedFile);

        return storedFile;
    }

    private void GivenTheCallerCanRead(StoredFile storedFile) =>
        _repository.GetReadableAsync(
                _callerId,
                storedFile.Id,
                Arg.Any<IReadOnlyCollection<FileCategory>>(),
                Arg.Any<CancellationToken>())
            .Returns(storedFile);

    private ValidatedHandler<IssueFileDownloadQuery, Result<FileDownload>> UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private static UserId AnotherUser() => UserId.Create(Guid.CreateVersion7());

    private ValidatedHandler<IssueFileDownloadQuery, Result<FileDownload>> UseCaseFor(ICurrentUser currentUser) => new(new IssueFileDownloadQueryHandler(new StoredFileService(_repository, currentUser, FilesSettings.Default().AsOptions()),
        _transfer,
        _content,
        NullLogger<IssueFileDownloadQueryHandler>.Instance).Handle, new IssueFileDownloadQueryValidator());
}
