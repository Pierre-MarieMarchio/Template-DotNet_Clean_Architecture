using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Services;

public sealed class StoredFileRegistrationServiceTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly FileCategory _image = FileCategory.Create(FilesSettings.Image);

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FilesOptions _files = FilesSettings.Default();

    public StoredFileRegistrationServiceTests()
    {
        _queries.GetUsageForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, 0, 0));
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStrictModeWrittenInLowerCase_TakesTheLock()
    {
        _files.Categories[FilesSettings.Image].Quota.Mode = "strict";
        var quotaLock = new RecordingOwnerQuotaLock();

        await RegisterAsync(quotaLock);

        quotaLock.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Strict_ReadsUsageInsertsAndCommitsWhileHoldingTheOwnersLock()
    {
        var quotaLock = new RecordingOwnerQuotaLock();
        var heldDuring = new List<string>();

        _queries.GetUsageForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Record(heldDuring, quotaLock, "read");

                return new OwnerStorageUsage(0, 0, 0, 0);
            });
        _repository.When(repository => repository.Add(Arg.Any<StoredFile>()))
            .Do(_ => Record(heldDuring, quotaLock, "add"));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Record(heldDuring, quotaLock, "commit");

            return 1;
        });

        var result = await RegisterAsync(quotaLock);

        result.IsSuccess.ShouldBeTrue();
        heldDuring.ShouldBe(["read", "add", "commit"]);
        quotaLock.Keys.ShouldBe([(_ownerId, _image)]);
        quotaLock.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Indicative_NeverTakesTheLock()
    {
        _files.Categories[FilesSettings.Image].Quota.Mode = FileCategoryQuotaOptions.IndicativeMode;
        var quotaLock = new RecordingOwnerQuotaLock();

        var result = await RegisterAsync(quotaLock);

        result.IsSuccess.ShouldBeTrue();
        quotaLock.Calls.ShouldBe(0);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Strict_AQuotaRefusal_WritesNothingAndReleasesTheLock()
    {
        var quotaLock = new RecordingOwnerQuotaLock();
        _queries.GetUsageForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, _files.Categories[FilesSettings.Image].Quota.MaxPendingUploads, 0));

        var result = await RegisterAsync(quotaLock);

        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
        quotaLock.IsHeld.ShouldBeFalse();
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Strict_ADomainRefusal_WritesNothingAndReleasesTheLock()
    {
        var quotaLock = new RecordingOwnerQuotaLock();

        var result = await RegisterAsync(quotaLock, name: "NUL.txt");

        result.Error!.Code.ShouldBe("domain.invariantViolated");
        quotaLock.IsHeld.ShouldBeFalse();
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
    }

    [Fact]
    public async Task Strict_AFailedCommit_PropagatesAndReleasesTheLock()
    {
        var quotaLock = new RecordingOwnerQuotaLock();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the database went away"));

        await Should.ThrowAsync<InvalidOperationException>(() => RegisterAsync(quotaLock));

        quotaLock.IsHeld.ShouldBeFalse();

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        (await RegisterAsync(quotaLock).WaitAsync(TimeSpan.FromSeconds(30), TestToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Strict_ALockThatTimesOut_IsQuotaBusyAndWritesNothing()
    {
        var quotaLock = new RecordingOwnerQuotaLock { TimesOut = true };

        var result = await RegisterAsync(quotaLock);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.quotaBusy");
        await _queries.DidNotReceive().GetUsageForOwnerAsync(
            Arg.Any<UserId>(),
            Arg.Any<FileCategory>(),
            Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUndeclaredCategory_IsAProgrammingError() =>
        await Should.ThrowAsync<InvalidOperationException>(() => Service(new RecordingOwnerQuotaLock())
            .RegisterAsync(
                _ownerId,
                FileCategory.Create("videos"),
                "holiday.png",
                AStoredFile.MediaType,
                AStoredFile.SizeInBytes,
                AStoredFile.Checksum,
                TestToken));

    [Fact]
    public async Task Withdraw_DeletesTheFileRemovesItAndCommits()
    {
        var storedFile = AStoredFile.PendingOwnedBy(_ownerId);

        await Service(new RecordingOwnerQuotaLock()).WithdrawAsync(storedFile, TestToken);

        storedFile.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
        _repository.Received(1).Remove(storedFile);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static void Record(List<string> steps, RecordingOwnerQuotaLock quotaLock, string step)
    {
        if (quotaLock.IsHeld)
        {
            steps.Add(step);
        }
    }

    private Task<Core.Common.Results.Result<StoredFile>> RegisterAsync(
        RecordingOwnerQuotaLock quotaLock,
        string name = "holiday.png") =>
        Service(quotaLock).RegisterAsync(
            _ownerId,
            _image,
            name,
            AStoredFile.MediaType,
            AStoredFile.SizeInBytes,
            AStoredFile.Checksum,
            TestToken);

    private StoredFileRegistrationService Service(RecordingOwnerQuotaLock quotaLock) => new(
        _repository,
        _queries,
        quotaLock,
        _unitOfWork,
        new StubDateTimeProvider(),
        _files.AsOptions());
}
