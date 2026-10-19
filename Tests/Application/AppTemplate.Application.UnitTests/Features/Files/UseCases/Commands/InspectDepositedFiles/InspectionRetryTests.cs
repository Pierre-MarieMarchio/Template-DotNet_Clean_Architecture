using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.InspectDepositedFiles;

/// <summary>
/// The inspection pass over a store that keeps state between passes: what happens to files that get
/// no verdict, and what one file's failed commit costs the others.
/// </summary>
public sealed class InspectionRetryTests
{
    private const int _batchSize = InspectDepositedFilesUseCase.BatchSize;

    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly DateTimeOffset _start = StubDateTimeProvider.DefaultInstant;

    private readonly InMemoryStoredFileStore _store = new();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IFileContentScanner _scanner = Substitute.For<IFileContentScanner>();

    public InspectionRetryTests() =>
        _scanner.ScanAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(FileScanOutcome.Clean);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region No file holds the front of the queue

    /// <summary>
    /// Reproduction: before deferral, the batch was the 20 oldest deposited files, so 20 files whose
    /// bytes are missing were the whole batch of every pass and the later deposit was never opened.
    /// </summary>
    [Fact]
    public async Task ABatchOfFilesWhoseBytesAreMissing_DoesNotStarveALaterDeposit()
    {
        for (int i = 0; i < _batchSize; i++)
        {
            var missing = Seed(_start.AddSeconds(i));
            _content.OpenReadAsync(missing.ObjectKey.Value, Arg.Any<CancellationToken>()).Returns((Stream?)null);
        }

        var later = Seed(_start.AddMinutes(1));
        GivenCleanContent(later);

        for (int pass = 0; pass < 5; pass++)
        {
            await RunPassAsync(_start.AddMinutes(5).AddHours(pass));
        }

        _store.Committed(later.Id).State.ShouldBe(StoredFileState.Available);
    }

    /// <summary>The same starvation with the scanner, rather than the store, failing for those files only.</summary>
    [Fact]
    public async Task ABatchOfFilesTheScannerCannotReach_DoesNotStarveALaterDeposit()
    {
        for (int i = 0; i < _batchSize; i++)
        {
            var unscannable = Seed(_start.AddSeconds(i));
            GivenCleanContent(unscannable);
            _scanner.ScanAsync(unscannable.ObjectKey.Value, Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(FileScanOutcome.Unavailable);
        }

        var later = Seed(_start.AddMinutes(1));
        GivenCleanContent(later);

        await RunPassAsync(_start.AddMinutes(5));
        await RunPassAsync(_start.AddMinutes(10));

        _store.Committed(later.Id).State.ShouldBe(StoredFileState.Available);
    }

    #endregion

    #region Deferral

    /// <summary>An outage defers; it never refuses, and the deferral survives into the next scope.</summary>
    [Fact]
    public async Task AFileWithoutAVerdict_IsDeferredAndStaysDeposited()
    {
        var missing = Seed(_start);
        _content.OpenReadAsync(missing.ObjectKey.Value, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        await RunPassAsync(_start.AddMinutes(5));

        var stored = _store.Committed(missing.Id);
        stored.State.ShouldBe(StoredFileState.Deposited);
        stored.InspectionAttempts.ShouldBe(1);
        stored.NextInspectionAt.ShouldBe(_start.AddMinutes(6));
    }

    /// <summary>Not offered before it is due, offered again once it is, and the wait doubles.</summary>
    [Fact]
    public async Task ADeferredFile_IsOfferedAgainOnceDue_WithADoubledWait()
    {
        var missing = Seed(_start);
        _content.OpenReadAsync(missing.ObjectKey.Value, Arg.Any<CancellationToken>()).Returns((Stream?)null);

        await RunPassAsync(_start);
        await RunPassAsync(_start.AddSeconds(30));

        _store.Committed(missing.Id).InspectionAttempts.ShouldBe(1);

        await RunPassAsync(_start.AddMinutes(1));

        var stored = _store.Committed(missing.Id);
        stored.InspectionAttempts.ShouldBe(2);
        stored.NextInspectionAt.ShouldBe(_start.AddMinutes(3));
    }

    /// <summary>However many attempts failed, the next one is at most an hour away.</summary>
    [Fact]
    public async Task AFileThatKeepsFailing_IsOfferedAgainAtLeastHourly()
    {
        var missing = Seed(_start);
        _content.OpenReadAsync(missing.ObjectKey.Value, Arg.Any<CancellationToken>()).Returns((Stream?)null);
        var now = _start;
        var wait = TimeSpan.Zero;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            await RunPassAsync(now);
            var next = _store.Committed(missing.Id).NextInspectionAt!.Value;
            wait = next - now;
            wait.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(1));
            now = next;
        }

        wait.ShouldBe(TimeSpan.FromHours(1));
        _store.Committed(missing.Id).InspectionAttempts.ShouldBe(12);
        _store.Committed(missing.Id).State.ShouldBe(StoredFileState.Deposited);
    }

    #endregion

    #region One commit per file

    /// <summary>
    /// A conflict on one file ends the pass, since its failed write stays staged; the verdicts already
    /// committed are kept, and the next pass reads the rest afresh.
    /// </summary>
    [Fact]
    public async Task AConflictOnOneFile_KeepsTheVerdictsCommittedBeforeIt()
    {
        var first = Seed(_start);
        var conflicting = Seed(_start.AddSeconds(1));
        var last = Seed(_start.AddSeconds(2));
        GivenCleanContent(first);
        GivenCleanContent(last);
        _content.OpenReadAsync(conflicting.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _store.ChangeBehindTheBack(conflicting.Id);

                return new MemoryStream(_png, writable: false);
            });

        var result = await RunPassAsync(_start.AddMinutes(5));

        result.ShouldBe(1);
        _store.Committed(first.Id).State.ShouldBe(StoredFileState.Available);
        _store.Committed(conflicting.Id).State.ShouldBe(StoredFileState.Deposited);
        _store.Committed(last.Id).State.ShouldBe(StoredFileState.Deposited);

        GivenCleanContent(conflicting);
        await RunPassAsync(_start.AddMinutes(6));

        _store.Committed(conflicting.Id).State.ShouldBe(StoredFileState.Available);
        _store.Committed(last.Id).State.ShouldBe(StoredFileState.Available);
    }

    [Fact]
    public async Task EveryFile_IsCommittedOnItsOwn()
    {
        GivenCleanContent(Seed(_start));
        GivenCleanContent(Seed(_start.AddSeconds(1)));
        GivenCleanContent(Seed(_start.AddSeconds(2)));

        await RunPassAsync(_start.AddMinutes(5));

        _store.Commits.ShouldBe(3);
    }

    #endregion

    private StoredFile Seed(DateTimeOffset registeredAt)
    {
        var storedFile = AStoredFile.DepositedOwnedBy(_ownerId, registeredAt: registeredAt);
        _store.Seed(storedFile);

        return storedFile;
    }

    private void GivenCleanContent(StoredFile storedFile) =>
        _content.OpenReadAsync(storedFile.ObjectKey.Value, Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(_png, writable: false));

    /// <summary>One pass in a fresh scope, as the background job runs it.</summary>
    private async Task<int> RunPassAsync(DateTimeOffset now)
    {
        _store.StartNewScope();

        var result = await new InspectDepositedFilesUseCase(
            _store,
            _content,
            _scanner,
            _store,
            new StubDateTimeProvider(now),
            new StubLeaderLease(),
            FilesSettings.Default().AsOptions(),
            NullLogger<InspectDepositedFilesUseCase>.Instance).ExecuteAsync(TestToken);

        return result.Value;
    }
}
