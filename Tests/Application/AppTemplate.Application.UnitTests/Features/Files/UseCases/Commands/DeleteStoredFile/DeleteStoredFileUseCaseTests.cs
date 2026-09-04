using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.DeleteStoredFile;

public sealed class DeleteStoredFileUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication and ownership

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new DeleteStoredFileCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_DeletesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new DeleteStoredFileCommand(Guid.CreateVersion7()), TestToken);

        _repository.DidNotReceive().Remove(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ownership check on the most destructive operation the feature has. Deleting the
    /// <c>OwnerId</c> comparison lets any authenticated caller destroy any file, and turns this red.
    /// </summary>
    [Fact]
    public async Task AnotherUsersFile_IsNotDeleted()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        _repository.DidNotReceive().Remove(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Not deleting is not enough: a refusal that announced the deletion would have a consumer
    /// reclaim somebody else's bytes while their row stayed exactly where it was.
    /// </summary>
    [Fact]
    public async Task AnotherUsersFile_HasNoDeletionAnnouncedForIt()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        await UseCase().ExecuteAsync(new DeleteStoredFileCommand(foreign.Id), TestToken);

        foreign.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnotherUsersFile_IsIndistinguishableFromAMissingOne()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var foreignResult = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(foreign.Id), TestToken);
        var missingResult = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(missingId), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    #endregion

    #region Success

    /// <summary>
    /// Deleting a file is removing its row — there is no state to move to and no flag to set. If
    /// this feature ever grew a "deleted" state, the row would stay and this would go red.
    /// </summary>
    [Fact]
    public async Task TheCallersOwnFile_HasItsRowRemovedAndCommitted()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        var result = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Remove(storedFile);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The announcement is the fast path that reclaims the bytes now instead of at the next sweep.
    /// It is raised alongside the removal, never instead of it.
    /// </summary>
    [Fact]
    public async Task TheCallersOwnFile_AnnouncesItsDeletionForTheContentToBeReclaimed()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        await UseCase().ExecuteAsync(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        var announcement = storedFile.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
        announcement.StoredFileId.ShouldBe(storedFile.Id);
        announcement.OwnerId.ShouldBe(_callerId);
        announcement.ObjectKey.ShouldBe(storedFile.ObjectKey);
        announcement.OccurredOn.ShouldBe(StubDateTimeProvider.DefaultInstant);
    }

    /// <summary>
    /// The key travels on the event because by the time a consumer runs — after the commit — the row
    /// it would have read is gone.
    /// </summary>
    [Fact]
    public async Task TheAnnouncement_CarriesTheKeyRatherThanLeavingItToBeLookedUp()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        await UseCase().ExecuteAsync(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        storedFile.DomainEvents
            .OfType<StoredFileDeletedDomainEvent>()
            .Single()
            .ObjectKey.Value.ShouldBe(storedFile.ObjectKey.Value);
    }

    [Fact]
    public async Task APendingFile_IsDeletableToo()
    {
        var storedFile = AStoredFile.PendingOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        var result = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Remove(storedFile);
    }

    [Fact]
    public async Task AMissingFile_IsReportedAsNotFoundRatherThanThrown()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var result = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(missingId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AnEmptyId_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(new DeleteStoredFileCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ANullCommand_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

    private StoredFile GivenTheCallerOwnsAFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private DeleteStoredFileUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private DeleteStoredFileUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new StoredFileService(_repository, currentUser),
        _repository,
        _unitOfWork,
        new StubDateTimeProvider(),
        new DeleteStoredFileCommandValidator());
}
