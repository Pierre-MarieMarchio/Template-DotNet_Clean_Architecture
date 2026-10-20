using System.Text;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.InspectDepositedFiles;

public sealed class InspectDepositedFilesUseCaseTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private static readonly byte[] _svg =
        Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>");

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IFileContentScanner _scanner = Substitute.For<IFileContentScanner>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FilesOptions _files = FilesSettings.Default();
    private StubLeaderLease _lease = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADepositWhoseContentMatchesWhatWasDeclared_IsMadeAvailable()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        storedFile.State.ShouldBe(StoredFileState.Available);
        storedFile.AvailableAt.ShouldBe(StubDateTimeProvider.DefaultInstant);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReleasedFile_RaisesTheMadeAvailableEvent()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png);

        await UseCase().ExecuteAsync(TestToken);

        storedFile.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StoredFileMadeAvailableDomainEvent>();
    }

    [Fact]
    public async Task ADepositWhoseContentContradictsWhatWasDeclared_IsQuarantined()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _svg);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        storedFile.State.ShouldBe(StoredFileState.Quarantined);
        storedFile.AvailableAt.ShouldBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInfectedDeposit_IsQuarantined()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png, FileScanOutcome.Infected("Eicar-Test-Signature"));

        await UseCase().ExecuteAsync(TestToken);

        storedFile.State.ShouldBe(StoredFileState.Quarantined);
        storedFile.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StoredFileQuarantinedDomainEvent>();
    }

    [Fact]
    public async Task AQuarantinedFile_KeepsNamingItsBytes()
    {
        var storedFile = GivenOneDepositedFile();
        var keyBefore = storedFile.ObjectKey;
        GivenTheContent(storedFile, _png, FileScanOutcome.Infected("Eicar-Test-Signature"));

        await UseCase().ExecuteAsync(TestToken);

        storedFile.ObjectKey.ShouldBe(keyBefore);
    }

    [Fact]
    public async Task AFileThatCouldNotBeInspected_IsDeferredAndTheDeferralCommitted()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png, FileScanOutcome.Unavailable);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        storedFile.State.ShouldBe(StoredFileState.Deposited);
        storedFile.DomainEvents.ShouldBeEmpty();
        storedFile.InspectionAttempts.ShouldBe(1);
        storedFile.NextInspectionAt.ShouldBe(StubDateTimeProvider.DefaultInstant.AddMinutes(1));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileWhoseContentIsMissing_IsLeftForTheNextPass()
    {
        var storedFile = GivenOneDepositedFile();
        _content.OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        storedFile.State.ShouldBe(StoredFileState.Deposited);
        await _scanner.DidNotReceive().ScanAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatCannotBeRead_IsAnOutageRatherThanAVerdict()
    {
        var storedFile = GivenOneDepositedFile();
        _content.OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns<Task<Stream?>>(_ => throw new IOException("connection reset"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        storedFile.State.ShouldBe(StoredFileState.Deposited);
        storedFile.InspectionAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task TheCallersCancellation_Propagates()
    {
        var storedFile = GivenOneDepositedFile();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _content.OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns<Task<Stream?>>(_ => throw new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => UseCase().ExecuteAsync(cancellation.Token));
    }

    [Fact]
    public async Task OneFileWithoutAVerdict_DoesNotStopTheOthers()
    {
        var stuck = ADepositedFile();
        var clean = ADepositedFile();
        GivenTheRepositoryHolds(stuck, clean);
        GivenTheContent(stuck, _png, FileScanOutcome.Unavailable);
        GivenTheContent(clean, _png);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        stuck.State.ShouldBe(StoredFileState.Deposited);
        clean.State.ShouldBe(StoredFileState.Available);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APassWithNothingToInspect_CommitsNothing()
    {
        GivenTheRepositoryHolds();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _content.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheContent_IsReadFromTheAggregatesOwnKey()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png);

        await UseCase().ExecuteAsync(TestToken);

        await _content.Received(1).OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>());
        await _scanner.Received(1)
            .ScanAsync(storedFile.ObjectKey.Value, Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheScanner_ReceivesTheWholeContentHeadIncluded()
    {
        byte[] large = [.. _png, .. Enumerable.Repeat((byte)'x', 5_000)];
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, large);
        byte[]? scanned = null;
        _scanner.ScanAsync(storedFile.ObjectKey.Value, Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var copy = new MemoryStream();
                call.Arg<Stream>().CopyTo(copy);
                scanned = copy.ToArray();

                return FileScanOutcome.Clean;
            });

        await UseCase().ExecuteAsync(TestToken);

        scanned.ShouldBe(large);
    }

    [Fact]
    public async Task EachFile_IsCommittedOnItsOwn()
    {
        var first = ADepositedFile();
        var second = ADepositedFile();
        GivenTheRepositoryHolds(first, second);
        GivenTheContent(first, _png);
        GivenTheContent(second, _svg);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(2);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheBatch_IsAskedForAsOfNow()
    {
        GivenTheRepositoryHolds();

        await UseCase().ExecuteAsync(TestToken);

        await _repository.Received(1).GetDepositedAsync(
            StubDateTimeProvider.DefaultInstant,
            InspectDepositedFilesUseCase.BatchSize,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLeaseHeldByAnotherHost_LeavesThePassUndone()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png);
        _lease = StubLeaderLease.HeldElsewhere();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        storedFile.State.ShouldBe(StoredFileState.Deposited);
        await _repository.DidNotReceive().GetDepositedAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLeaseName_IsPinnedAsALiteral()
    {
        GivenTheRepositoryHolds();

        await UseCase().ExecuteAsync(TestToken);

        _lease.RequestedNames.ShouldBe(["apptemplate.files.inspect-deposited"]);
    }

    [Fact]
    public async Task TheScanner_IsGivenTheConfirmedSize()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png);

        await UseCase().ExecuteAsync(TestToken);

        await _scanner.Received(1).ScanAsync(
            storedFile.ObjectKey.Value,
            Arg.Any<Stream>(),
            storedFile.Size.Bytes,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACategoryThatIsNotScanned_IsReleasedWithoutCallingTheScanner()
    {
        _files.Categories[FilesSettings.Document].Scan = false;
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png, FileScanOutcome.Infected("never asked"));

        await UseCase().ExecuteAsync(TestToken);

        storedFile.State.ShouldBe(StoredFileState.Available);
        await _scanner.DidNotReceive().ScanAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACategoryThatDoesNotVerifySignatures_ReleasesAHeadThatNamesAnotherType()
    {
        _files.Categories[FilesSettings.Document].VerifySignature = false;
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, Encoding.ASCII.GetBytes("id,total\n1,9.99\n"));

        await UseCase().ExecuteAsync(TestToken);

        storedFile.State.ShouldBe(StoredFileState.Available);
    }

    [Fact]
    public async Task ACategoryThatDoesNotVerifySignatures_StillQuarantinesMarkup()
    {
        _files.Categories[FilesSettings.Document].VerifySignature = false;
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _svg);

        await UseCase().ExecuteAsync(TestToken);

        storedFile.State.ShouldBe(StoredFileState.Quarantined);
    }

    [Fact]
    public async Task ACategoryNoLongerConfigured_IsScannedAndVerified()
    {
        var storedFile = AStoredFile.DepositedOwnedBy(_ownerId, category: "retired");
        GivenTheRepositoryHolds(storedFile);
        GivenTheContent(storedFile, Encoding.ASCII.GetBytes("id,total\n1,9.99\n"));

        await UseCase().ExecuteAsync(TestToken);

        storedFile.State.ShouldBe(StoredFileState.Quarantined);
        await _scanner.Received(1).ScanAsync(
            storedFile.ObjectKey.Value,
            Arg.Any<Stream>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    private static StoredFile ADepositedFile() => AStoredFile.DepositedOwnedBy(_ownerId);

    private StoredFile GivenOneDepositedFile()
    {
        var storedFile = ADepositedFile();
        GivenTheRepositoryHolds(storedFile);

        return storedFile;
    }

    private void GivenTheRepositoryHolds(params StoredFile[] files) =>
        _repository.GetDepositedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(files);

    private void GivenTheContent(StoredFile storedFile, byte[] bytes, FileScanOutcome? scan = null)
    {
        _content.OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(bytes, writable: false));
        _scanner.ScanAsync(storedFile.ObjectKey.Value, Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(scan ?? FileScanOutcome.Clean);
    }

    private InspectDepositedFilesUseCase UseCase() => new(
        _repository,
        _content,
        _scanner,
        _unitOfWork,
        new StubDateTimeProvider(),
        _lease,
        _files.AsOptions(),
        NullLogger<InspectDepositedFilesUseCase>.Instance);
}
