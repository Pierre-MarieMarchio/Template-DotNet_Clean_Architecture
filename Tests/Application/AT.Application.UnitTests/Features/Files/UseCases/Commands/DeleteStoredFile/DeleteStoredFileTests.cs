using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.DeleteStoredFile;

public sealed class DeleteStoredFileTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .Handle(new DeleteStoredFileCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_DeletesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .Handle(new DeleteStoredFileCommand(Guid.CreateVersion7()), TestToken);

        _repository.DidNotReceive().Remove(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCallersOwnFile_HasItsRowRemovedAndCommitted()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        var result = await UseCase().Handle(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Remove(storedFile);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCallersOwnFile_AnnouncesItsDeletionForTheContentToBeReclaimed()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        await UseCase().Handle(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        var announcement = storedFile.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
        announcement.StoredFileId.ShouldBe(storedFile.Id);
        announcement.OwnerId.ShouldBe(_callerId);
        announcement.ObjectKey.ShouldBe(storedFile.ObjectKey);
        announcement.OccurredOn.ShouldBe(TheClock.DefaultInstant);
    }

    [Fact]
    public async Task TheAnnouncement_CarriesTheKeyRatherThanLeavingItToBeLookedUp()
    {
        var storedFile = GivenTheCallerOwnsAFile();

        await UseCase().Handle(new DeleteStoredFileCommand(storedFile.Id), TestToken);

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

        var result = await UseCase().Handle(new DeleteStoredFileCommand(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Remove(storedFile);
    }

    [Fact]
    public async Task AMissingFile_IsReportedAsNotFoundRatherThanThrown()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var result = await UseCase().Handle(new DeleteStoredFileCommand(missingId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AnEmptyId_IsAValidationFailure()
    {
        var result = await UseCase().Handle(new DeleteStoredFileCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    private StoredFile GivenTheCallerOwnsAFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private ValidatedHandler<DeleteStoredFileCommand, Result> UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private ValidatedHandler<DeleteStoredFileCommand, Result> UseCaseFor(ICurrentUser currentUser) => new(new DeleteStoredFileCommandHandler(new StoredFileService(_repository, currentUser, FilesSettings.Default().AsOptions()),
        _repository,
        _unitOfWork,
        TheClock.Start()).Handle, new DeleteStoredFileCommandValidator());
}
