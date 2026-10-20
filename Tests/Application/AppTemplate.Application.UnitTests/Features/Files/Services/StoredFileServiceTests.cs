using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Services;

public sealed class StoredFileServiceTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await AccessFor(StubCurrentUser.Anonymous)
            .LoadOwnedAsync(Guid.CreateVersion7(), precondition: null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_NeverReachesTheRepository()
    {
        await AccessFor(StubCurrentUser.Anonymous).LoadOwnedAsync(Guid.CreateVersion7(), null, TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownId_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var result = await Access().LoadOwnedAsync(missingId, null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("storedFile.notFound");
    }

    [Fact]
    public async Task AnotherUsersFile_IsIndistinguishableFromAMissingOne()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var foreignResult = await Access().LoadOwnedAsync(foreign.Id, null, TestToken);
        var missingResult = await Access().LoadOwnedAsync(missingId, null, TestToken);

        foreignResult.IsFailure.ShouldBeTrue();
        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
        foreignResult.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AnotherUsersFile_IsNotNamedInTheRefusal()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await Access().LoadOwnedAsync(foreign.Id, null, TestToken);

        result.Error!.Message.ShouldNotContain(foreign.Name.Value);
        result.Error.Message.ShouldNotContain(foreign.ObjectKey.Value);
    }

    [Fact]
    public async Task ANullPrecondition_LeavesTheLoadUnconditional()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        var result = await Access().LoadOwnedAsync(storedFile.Id, precondition: null, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(storedFile);
    }

    [Fact]
    public async Task ASatisfiedPrecondition_Succeeds()
    {
        var storedFile = AStoredFile.AvailableOwnedByAtVersion(_callerId, version: 7);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        var result = await Access().LoadOwnedAsync(storedFile.Id, new VersionPrecondition([7]), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(storedFile);
    }

    [Fact]
    public async Task AnUnsatisfiedPrecondition_IsReportedAsAPreconditionFailure()
    {
        var storedFile = AStoredFile.AvailableOwnedByAtVersion(_callerId, version: 7);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        var result = await Access().LoadOwnedAsync(storedFile.Id, new VersionPrecondition([6]), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    [Fact]
    public async Task AnotherUsersFile_IsRefusedAsMissingEvenWithAnUnsatisfiablePrecondition()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await Access().LoadOwnedAsync(foreign.Id, new VersionPrecondition([999]), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    #region Reading content

    [Fact]
    public async Task Readable_AnAnonymousCallerIsRefused()
    {
        var result = await AccessFor(StubCurrentUser.Anonymous).LoadReadableAsync(Guid.CreateVersion7(), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Readable_TheOwnersPendingFileIsLoaded()
    {
        var pending = AStoredFile.PendingOwnedBy(_callerId);
        _repository.GetAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await Access().LoadReadableAsync(pending.Id, TestToken);

        result.Value.ShouldBeSameAs(pending);
    }

    [Fact]
    public async Task Readable_AnotherUsersAvailableFileInAnAuthenticatedCategoryIsLoaded()
    {
        var shared = AStoredFile.AvailableOwnedBy(UserId.Create(Guid.CreateVersion7()), category: FilesSettings.Image);
        _repository.GetAsync(shared.Id, Arg.Any<CancellationToken>()).Returns(shared);

        var result = await Access().LoadReadableAsync(shared.Id, TestToken);

        result.Value.ShouldBeSameAs(shared);
    }

    [Fact]
    public async Task Readable_AnotherUsersPendingFileInAnAuthenticatedCategoryIsNotFound()
    {
        var pending = AStoredFile.PendingOwnedBy(UserId.Create(Guid.CreateVersion7()), category: FilesSettings.Image);
        _repository.GetAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await Access().LoadReadableAsync(pending.Id, TestToken);

        result.Error!.Code.ShouldBe("storedFile.notFound");
    }

    [Fact]
    public async Task Readable_AnotherUsersFileInAPrivateCategoryIsNotFound()
    {
        var foreign = AStoredFile.AvailableOwnedBy(UserId.Create(Guid.CreateVersion7()), category: FilesSettings.Document);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await Access().LoadReadableAsync(foreign.Id, TestToken);

        result.Error!.Code.ShouldBe("storedFile.notFound");
    }

    [Fact]
    public async Task Owned_AnotherUsersFileInAnAuthenticatedCategoryIsStillNotFound()
    {
        var shared = AStoredFile.AvailableOwnedBy(UserId.Create(Guid.CreateVersion7()), category: FilesSettings.Image);
        _repository.GetAsync(shared.Id, Arg.Any<CancellationToken>()).Returns(shared);

        var result = await Access().LoadOwnedAsync(shared.Id, precondition: null, TestToken);

        result.Error!.Code.ShouldBe("storedFile.notFound");
    }

    #endregion

    private StoredFile GivenTheCallerOwnsAFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private StoredFileService Access() => AccessFor(StubCurrentUser.WithId(_callerId));

    private StoredFileService AccessFor(StubCurrentUser currentUser) =>
        new(_repository, currentUser, FilesSettings.Default().AsOptions());
}
