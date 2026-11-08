using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
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

        await _repository.DidNotReceive().GetAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownId_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(_callerId, missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var result = await Access().LoadOwnedAsync(missingId, null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("storedFile.notFound");
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
        _repository.GetAsync(_callerId, storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        var result = await Access().LoadOwnedAsync(storedFile.Id, new VersionPrecondition([7]), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(storedFile);
    }

    [Fact]
    public async Task AnUnsatisfiedPrecondition_IsReportedAsAPreconditionFailure()
    {
        var storedFile = AStoredFile.AvailableOwnedByAtVersion(_callerId, version: 7);
        _repository.GetAsync(_callerId, storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        var result = await Access().LoadOwnedAsync(storedFile.Id, new VersionPrecondition([6]), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    [Fact]
    public async Task Readable_AnAnonymousCallerIsRefused()
    {
        var result = await AccessFor(StubCurrentUser.Anonymous).LoadReadableAsync(Guid.CreateVersion7(), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Readable_WhatTheReadableQueryFindsIsLoaded()
    {
        var shared = AStoredFile.AvailableOwnedBy(UserId.Create(Guid.CreateVersion7()), category: FilesSettings.Image);
        _repository.GetReadableAsync(
                _callerId,
                shared.Id,
                Arg.Any<IReadOnlyCollection<FileCategory>>(),
                Arg.Any<CancellationToken>())
            .Returns(shared);

        var result = await Access().LoadReadableAsync(shared.Id, TestToken);

        result.Value.ShouldBeSameAs(shared);
    }

    [Fact]
    public async Task Readable_WhatTheReadableQueryDoesNotFindIsNotFound()
    {
        var result = await Access().LoadReadableAsync(Guid.CreateVersion7(), TestToken);

        result.Error!.Code.ShouldBe("storedFile.notFound");
    }

    [Fact]
    public async Task Readable_OpensToOtherCallersOnlyTheCategoriesConfiguredAsAuthenticated()
    {
        IReadOnlyCollection<FileCategory>? opened = null;
        _repository.GetReadableAsync(
                _callerId,
                Arg.Any<Guid>(),
                Arg.Do<IReadOnlyCollection<FileCategory>>(categories => opened = categories),
                Arg.Any<CancellationToken>())
            .Returns((StoredFile?)null);

        await Access().LoadReadableAsync(Guid.CreateVersion7(), TestToken);

        opened.ShouldNotBeNull().Select(category => category.Value).ShouldBe([FilesSettings.Image]);
    }

    [Fact]
    public async Task Owned_NeverFallsBackOnTheReadableQuery()
    {
        var shared = AStoredFile.AvailableOwnedBy(UserId.Create(Guid.CreateVersion7()), category: FilesSettings.Image);
        _repository.GetReadableAsync(
                _callerId,
                shared.Id,
                Arg.Any<IReadOnlyCollection<FileCategory>>(),
                Arg.Any<CancellationToken>())
            .Returns(shared);

        var result = await Access().LoadOwnedAsync(shared.Id, precondition: null, TestToken);

        result.Error!.Code.ShouldBe("storedFile.notFound");
    }

    private StoredFile GivenTheCallerOwnsAFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(_callerId, storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private StoredFileService Access() => AccessFor(StubCurrentUser.WithId(_callerId));

    private StoredFileService AccessFor(StubCurrentUser currentUser) =>
        new(_repository, currentUser, FilesSettings.Default().AsOptions());
}
