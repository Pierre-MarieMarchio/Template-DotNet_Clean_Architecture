using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.IssueFileDownload;

public sealed class IssueFileDownloadUseCaseTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private static readonly IssuedDownloadGrant _grant = new(
        "https://store.example/download",
        StubDateTimeProvider.DefaultInstant.AddMinutes(5));

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileDirectTransfer _transfer = Substitute.For<IFileDirectTransfer>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();

    public IssueFileDownloadUseCaseTests()
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
            .ExecuteAsync(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_GetsNoGrant()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AnotherUsersFile_YieldsNoGrant()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AnotherUsersFile_IsIndistinguishableFromAMissingOne()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var foreignResult = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(foreign.Id), TestToken);
        var missingResult = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(missingId), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    [Fact]
    public async Task AFileWhoseDepositWasNeverConfirmed_YieldsNoGrant()
    {
        var pending = AStoredFile.PendingOwnedBy(_callerId);
        _repository.GetAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(pending.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AFileWaitingForAVerdictOnItsContent_YieldsNoGrant()
    {
        var deposited = AStoredFile.DepositedOwnedBy(_callerId);
        _repository.GetAsync(deposited.Id, Arg.Any<CancellationToken>()).Returns(deposited);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(deposited.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AQuarantinedFile_CanNeverBeServed()
    {
        var quarantined = AStoredFile.QuarantinedOwnedBy(_callerId);
        _repository.GetAsync(quarantined.Id, Arg.Any<CancellationToken>()).Returns(quarantined);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(quarantined.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AQuarantinedFile_IsDistinguishableFromOneStillWaiting()
    {
        var quarantined = AStoredFile.QuarantinedOwnedBy(_callerId);
        var deposited = AStoredFile.DepositedOwnedBy(_callerId);
        _repository.GetAsync(quarantined.Id, Arg.Any<CancellationToken>()).Returns(quarantined);
        _repository.GetAsync(deposited.Id, Arg.Any<CancellationToken>()).Returns(deposited);

        var refused = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(quarantined.Id), TestToken);
        var waiting = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(deposited.Id), TestToken);

        refused.Error!.Code.ShouldBe("storedFile.quarantined");
        refused.Error.Code.ShouldNotBe(waiting.Error!.Code);
        refused.Error.Message.ShouldNotContain("virus", Case.Insensitive);
        refused.Error.Message.ShouldNotContain("svg", Case.Insensitive);
    }

    [Fact]
    public async Task TheCallersOwnAvailableFile_YieldsAGrant()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

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

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Redirect.ShouldBeNull();
        result.Value.Content.ShouldBeSameAs(bytes);
        result.Value.FileName.ShouldBe(storedFile.Name.Value);
        result.Value.MediaType.ShouldBe(storedFile.DeclaredMediaType.Value);
        result.Value.SizeInBytes.ShouldBe(storedFile.Size.Bytes);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AStoreThatCannotSign_OpensNothingForSomebodyElsesFile()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        _transfer.IsSupported.Returns(false);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(foreign.Id), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        await _content.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAvailableFileWithNoBytes_IsAnsweredAsNotAvailable()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();
        _transfer.IsSupported.Returns(false);
        _content.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task AnotherUsersAvailableFileInAnAuthenticatedCategory_YieldsAGrant()
    {
        var shared = AStoredFile.AvailableOwnedBy(AnotherUser(), category: FilesSettings.Image);
        _repository.GetAsync(shared.Id, Arg.Any<CancellationToken>()).Returns(shared);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(shared.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Redirect.ShouldBeSameAs(_grant);
    }

    [Fact]
    public async Task AnotherUsersUnavailableFileInAnAuthenticatedCategory_IsNotFound()
    {
        var waiting = AStoredFile.DepositedOwnedBy(AnotherUser(), category: FilesSettings.Image);
        _repository.GetAsync(waiting.Id, Arg.Any<CancellationToken>()).Returns(waiting);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(waiting.Id), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AnotherUsersFileInAPrivateCategory_IsNotFound()
    {
        var foreign = AStoredFile.AvailableOwnedBy(AnotherUser(), category: FilesSettings.Document);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(foreign.Id), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AnotherUsersFileInACategoryNoLongerConfigured_IsNotFound()
    {
        var orphaned = AStoredFile.AvailableOwnedBy(AnotherUser(), category: "retired");
        _repository.GetAsync(orphaned.Id, Arg.Any<CancellationToken>()).Returns(orphaned);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(orphaned.Id), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task TheOwnersFileInACategoryNoLongerConfigured_IsStillServed()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId, category: "retired");
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TheGrant_NamesTheAggregatesKeyAndOffersTheUsersLabel()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

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

        await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

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
        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(Guid.Empty), TestToken);

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
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private IssueFileDownloadUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private static UserId AnotherUser() => UserId.Create(Guid.CreateVersion7());

    private IssueFileDownloadUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new StoredFileService(_repository, currentUser, FilesSettings.Default().AsOptions()),
        _transfer,
        _content,
        new IssueFileDownloadQueryValidator(),
        NullLogger<IssueFileDownloadUseCase>.Instance);
}
