using System.Text;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
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

    #region A clean file

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

    /// <summary>
    /// The event the derivative hook hangs off, raised here rather than at confirmation — which is
    /// the moment it now means what it says: the file is servable, and a consumer reading its bytes
    /// is reading content something has looked at.
    /// </summary>
    [Fact]
    public async Task AReleasedFile_RaisesTheMadeAvailableEvent()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _png);

        await UseCase().ExecuteAsync(TestToken);

        storedFile.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StoredFileMadeAvailableDomainEvent>();
    }

    #endregion

    #region A refused file

    /// <summary>
    /// The end-to-end shape of the gap being closed: a client declared a PNG, deposited a script
    /// container, and the file is refused on the evidence of its own bytes rather than of its own
    /// claim.
    /// </summary>
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

    /// <summary>
    /// Quarantining removes nothing from the object store, and this asserts the pass does not go
    /// behind the aggregate's back and do it: the row goes on naming the same key, which is what
    /// leaves the bytes reachable and keeps deletion the only thing that reclaims them.
    /// </summary>
    [Fact]
    public async Task AQuarantinedFile_KeepsNamingItsBytes()
    {
        var storedFile = GivenOneDepositedFile();
        var keyBefore = storedFile.ObjectKey;
        GivenTheContent(storedFile, _png, FileScanOutcome.Infected("Eicar-Test-Signature"));

        await UseCase().ExecuteAsync(TestToken);

        storedFile.ObjectKey.ShouldBe(keyBefore);
    }

    #endregion

    #region No verdict

    /// <summary>
    /// A scanner that cannot be reached must not make files readable, and must not destroy them
    /// either. The file stays deposited, its next attempt is pushed out, and the deferral is committed.
    /// </summary>
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

    /// <summary>
    /// A confirmed deposit whose object is gone is no verdict, not a refusal: quarantining on an
    /// absence would refuse a file over a store fault.
    /// </summary>
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

    /// <summary>
    /// A store that fails while being read is an outage, and an outage is retried.
    /// </summary>
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

    /// <summary>
    /// Shutdown is not an outage: the caller's cancellation propagates instead of being logged as a
    /// file that could not be read.
    /// </summary>
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

    /// <summary>
    /// One unreachable file must not hold up the rest of the batch. The pass is the only thing that
    /// makes any upload readable, so a single object the store will not answer about cannot be
    /// allowed to stop every other user's file.
    /// </summary>
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

    #endregion

    #region The pass itself

    /// <summary>
    /// Nothing to inspect is the normal state of a healthy system, and it must cost nothing: no
    /// commit, and therefore no round trip to prove that nothing changed.
    /// </summary>
    [Fact]
    public async Task APassWithNothingToInspect_CommitsNothing()
    {
        GivenTheRepositoryHolds();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _content.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The content is read from the key the aggregate holds, never one recomputed from anything
    /// else. A pass that read the wrong object would clear a file on the strength of another file's
    /// bytes.
    /// </summary>
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

    /// <summary>
    /// The head is read off the front of the stream before the scan; the scanner must still receive
    /// every byte, head included, or it would clear an executable whose signature sits in the
    /// first kibibyte.
    /// </summary>
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

    /// <summary>One commit per file, so a failure on one file loses no other file's verdict.</summary>
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

    /// <summary>The batch is asked for as of now, so a deferred file comes back only once it is due.</summary>
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

    #endregion

    #region One host at a time

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

    /// <summary>The literal is repeated on purpose: the name is a contract across releases.</summary>
    [Fact]
    public async Task ThePass_RunsUnderItsNamedLease()
    {
        GivenTheRepositoryHolds();

        await UseCase().ExecuteAsync(TestToken);

        _lease.RequestedNames.ShouldBe(["apptemplate.files.inspect-deposited"]);
    }

    #endregion

    #region What the category decides

    /// <summary>The confirmed size goes with the content, so a scanner with a ceiling refuses before streaming.</summary>
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

    /// <summary>Markup is refused in every category, whatever it says about signatures.</summary>
    [Fact]
    public async Task ACategoryThatDoesNotVerifySignatures_StillQuarantinesMarkup()
    {
        _files.Categories[FilesSettings.Document].VerifySignature = false;
        var storedFile = GivenOneDepositedFile();
        GivenTheContent(storedFile, _svg);

        await UseCase().ExecuteAsync(TestToken);

        storedFile.State.ShouldBe(StoredFileState.Quarantined);
    }

    /// <summary>
    /// A category removed from configuration after registration gets the strictest inspection: the
    /// scanner is asked and the head must match.
    /// </summary>
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

    #endregion

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
