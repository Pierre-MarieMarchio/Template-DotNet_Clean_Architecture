using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Concurrent registrations of one owner in one category with room for one more file. Each
/// registration has its own repository and unit of work over one shared set of committed rows, and
/// a usage read does not return until every registration has read or one is waiting for the lock:
/// without a lock, all of them read usage before any commits.
/// </summary>
public sealed class RegisterFileConcurrencyTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// The reproduction: before registrations took the lock, both of these succeeded and two rows
    /// were committed against a limit of one.
    /// </summary>
    [Fact]
    public async Task Strict_TwoConcurrentRegistrationsWithRoomForOne_CommitExactlyOne()
    {
        var world = new SharedWorld(FileCategoryQuotaOptions.StrictMode, registrations: 2);

        var results = await world.RegisterAllAsync();

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Single(result => result.IsFailure).Error!.Code.ShouldBe("storedFile.quotaExceeded");
        world.CommittedCount.ShouldBe(1, "a strict quota of one pending upload must hold under concurrency.");
    }

    [Fact]
    public async Task Strict_ABurstOfRegistrations_NeverCommitsMoreThanTheLimit()
    {
        var world = new SharedWorld(FileCategoryQuotaOptions.StrictMode, registrations: 5);

        var results = await world.RegisterAllAsync();

        results.Count(result => result.IsSuccess).ShouldBe(1);
        world.CommittedCount.ShouldBe(1);
    }

    /// <summary>
    /// The documented bound of the indicative mode: a burst of N concurrent registrations overshoots
    /// by at most N - 1 files, here reached exactly, and never takes the lock.
    /// </summary>
    [Fact]
    public async Task Indicative_ABurstOfNRegistrations_OvershootsByAtMostNMinusOne()
    {
        const int burst = 3;
        var world = new SharedWorld(FileCategoryQuotaOptions.IndicativeMode, registrations: burst);

        var results = await world.RegisterAllAsync();

        results.Count(result => result.IsSuccess).ShouldBe(burst);
        world.CommittedCount.ShouldBe(1 + (burst - 1));
        world.Lock.Calls.ShouldBe(0);
    }

    private sealed class SharedWorld
    {
        private readonly List<StoredFile> _committed = [];
        private readonly TaskCompletionSource _allRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly FilesOptions _files;
        private readonly int _registrations;
        private int _reads;

        public SharedWorld(string mode, int registrations)
        {
            _registrations = registrations;
            _files = FilesSettings.With(options =>
            {
                options.Categories[FilesSettings.Image].Quota.Mode = mode;
                options.Categories[FilesSettings.Image].Quota.MaxPendingUploads = 1;
            });
        }

        public RecordingOwnerQuotaLock Lock { get; } = new();

        public int CommittedCount
        {
            get
            {
                lock (_committed)
                {
                    return _committed.Count;
                }
            }
        }

        public Task<Result<RegisterFileOutcome>[]> RegisterAllAsync() =>
            Task.WhenAll(Enumerable.Range(0, _registrations).Select(_ => RegisterAsync()));

        private async Task<Result<RegisterFileOutcome>> RegisterAsync()
        {
            await Task.Yield();

            StoredFile? staged = null;
            var repository = Substitute.For<IStoredFileRepository>();
            repository.When(r => r.Add(Arg.Any<StoredFile>())).Do(call => staged = call.Arg<StoredFile>());

            var unitOfWork = Substitute.For<IUnitOfWork>();
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                lock (_committed)
                {
                    _committed.Add(staged!);
                }

                return 1;
            });

            var queries = Substitute.For<IStoredFileQueries>();
            queries.GetUsageForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
                .Returns(_ => ReadUsageAsync());

            var transfer = Substitute.For<IFileDirectTransfer>();
            transfer.IsSupported.Returns(true);
            transfer.CreateUploadGrantAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<long>(),
                    Arg.Any<string>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>())
                .Returns(new IssuedUploadGrant(
                    "https://store.example/upload",
                    "PUT",
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    StubDateTimeProvider.DefaultInstant.AddMinutes(30)));

            var useCase = new RegisterFileUseCase(
                new StoredFileRegistrationService(
                    repository,
                    queries,
                    Lock,
                    unitOfWork,
                    new StubDateTimeProvider(),
                    _files.AsOptions()),
                transfer,
                StubCurrentUser.WithId(_callerId),
                _files.AsOptions(),
                new RegisterFileCommandValidator(_files.AsOptions()));

            return await useCase.ExecuteAsync(
                new RegisterFileCommand(
                    FilesSettings.Image,
                    "holiday.png",
                    AStoredFile.MediaType,
                    AStoredFile.SizeInBytes,
                    AStoredFile.Checksum),
                TestToken);
        }

        /// <summary>
        /// Reads committed usage, then holds the reader until every registration has read or one of
        /// them is waiting for the lock: the widest window an unlocked read-then-insert can have.
        /// </summary>
        private async Task<OwnerStorageUsage> ReadUsageAsync()
        {
            int pending = CommittedCount;

            if (Interlocked.Increment(ref _reads) >= _registrations)
            {
                _allRead.TrySetResult();
            }

            await Task.WhenAny(_allRead.Task, Lock.Contended).WaitAsync(TestToken);

            return new OwnerStorageUsage(0, 0, pending, pending * AStoredFile.SizeInBytes);
        }
    }
}
