using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Services;

/// <summary>
/// The one gate every file command loads its aggregate through, so its own tests are where the
/// identity/ownership/precondition matrix is proven exhaustively rather than re-proven, slightly
/// differently, in every use case's test file.
/// </summary>
public sealed class StoredFileAccessTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

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

    /// <summary>
    /// "Not yours" and "does not exist" answer identically, so a caller cannot use this to enumerate
    /// other users' file ids. Deleting the <c>OwnerId</c> comparison in <c>StoredFileAccess</c>
    /// turns this red, and every other ownership test in the feature with it.
    /// </summary>
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

    /// <summary>
    /// The message must not leak the file either: a 404 quoting somebody else's file name would say
    /// the file exists just as loudly as a 403 would.
    /// </summary>
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

    /// <summary>
    /// Ownership is answered before the version is, so a caller cannot learn that somebody else's
    /// file exists by watching a 412 come back instead of a 404.
    /// </summary>
    [Fact]
    public async Task AnotherUsersFile_IsRefusedAsMissingEvenWithAnUnsatisfiablePrecondition()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await Access().LoadOwnedAsync(foreign.Id, new VersionPrecondition([999]), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    private StoredFile GivenTheCallerOwnsAFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private StoredFileAccess Access() => AccessFor(StubCurrentUser.WithId(_callerId));

    private StoredFileAccess AccessFor(StubCurrentUser currentUser) => new(_repository, currentUser);
}
