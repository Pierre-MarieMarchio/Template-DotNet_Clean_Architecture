using System.Text;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.UnitTests.TestDoubles;
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
    private static readonly Guid _ownerId = Guid.CreateVersion7();
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private static readonly byte[] _svg =
        Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>");

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileContentInspector _inspector = Substitute.For<IFileContentInspector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region A clean file

    [Fact]
    public async Task ADepositWhoseContentMatchesWhatWasDeclared_IsMadeAvailable()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Clean, _png);

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
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Clean, _png);

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
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Clean, _svg);

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
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Infected, _png, "Eicar-Test-Signature");

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
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Infected, _png, "Eicar-Test-Signature");

        await UseCase().ExecuteAsync(TestToken);

        storedFile.ObjectKey.ShouldBe(keyBefore);
    }

    #endregion

    #region No verdict

    /// <summary>
    /// <b>The arbitrage, at the level of the pass.</b> A scanner that cannot be reached must not make
    /// files readable, and must not destroy them either. The file stays exactly where it was, nothing
    /// is committed, and the next pass will find it again.
    /// </summary>
    [Fact]
    public async Task AFileThatCouldNotBeInspected_IsLeftUntouchedAndNothingIsCommitted()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Unavailable, ReadOnlyMemory<byte>.Empty);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        storedFile.State.ShouldBe(StoredFileState.Deposited);
        storedFile.DomainEvents.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
        GivenTheInspectorReports(stuck, ContentInspectionStatus.Unavailable, ReadOnlyMemory<byte>.Empty);
        GivenTheInspectorReports(clean, ContentInspectionStatus.Clean, _png);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        stuck.State.ShouldBe(StoredFileState.Deposited);
        clean.State.ShouldBe(StoredFileState.Available);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
        await _inspector.DidNotReceive().InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The inspector is asked about the key the aggregate holds, never one recomputed from anything
    /// else. A pass that inspected the wrong object would clear a file on the strength of another
    /// file's bytes.
    /// </summary>
    [Fact]
    public async Task TheInspector_IsAskedAboutTheAggregatesOwnKey()
    {
        var storedFile = GivenOneDepositedFile();
        GivenTheInspectorReports(storedFile, ContentInspectionStatus.Clean, _png);

        await UseCase().ExecuteAsync(TestToken);

        await _inspector.Received(1)
            .InspectAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One commit for the batch rather than one per file. Each transition is independent of every
    /// other, so a failure loses a pass's decisions rather than corrupting any of them, and the next
    /// pass reaches the same verdicts from the same unchanged bytes.
    /// </summary>
    [Fact]
    public async Task OnePass_CommitsOnce()
    {
        var first = ADepositedFile();
        var second = ADepositedFile();
        GivenTheRepositoryHolds(first, second);
        GivenTheInspectorReports(first, ContentInspectionStatus.Clean, _png);
        GivenTheInspectorReports(second, ContentInspectionStatus.Clean, _svg);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
        _repository.GetDepositedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(files);

    private void GivenTheInspectorReports(
        StoredFile storedFile,
        ContentInspectionStatus status,
        ReadOnlyMemory<byte> head,
        string? signature = null) =>
        _inspector.InspectAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns(new ContentInspectionOutcome(status, head, signature));

    private InspectDepositedFilesUseCase UseCase() => new(
        _repository,
        _inspector,
        _unitOfWork,
        new StubDateTimeProvider(),
        NullLogger<InspectDepositedFilesUseCase>.Instance);
}
