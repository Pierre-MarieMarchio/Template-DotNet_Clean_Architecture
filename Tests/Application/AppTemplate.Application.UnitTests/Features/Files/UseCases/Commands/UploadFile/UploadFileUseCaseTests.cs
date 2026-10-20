using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.UploadFile;

public sealed class UploadFileUseCaseTests
{
    private const string _mediaType = "text/csv";

    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());
    private static readonly byte[] _bytes = Encoding.UTF8.GetBytes("id,name\n1,quarterly\n");
    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RecordingOwnerQuotaLock _quotaLock = new();
    private readonly RecordingLogger<UploadFileUseCase> _logger = new();
    private readonly FilesOptions _files = FilesSettings.Default();

    private StoredFile? _registered;
    private StoredFile? _removed;

    public UploadFileUseCaseTests()
    {
        _queries.GetUsageForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, 0, 0));

        _repository.When(repository => repository.Add(Arg.Any<StoredFile>()))
            .Do(call => _registered = call.Arg<StoredFile>());
        _repository.When(repository => repository.Remove(Arg.Any<StoredFile>()))
            .Do(call => _removed = call.Arg<StoredFile>());

        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ContentWriteOutcome.Written);

        _content.DescribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StoredObjectDescription(_bytes.Length, _checksum));
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUpload_EndsDeposited_AsADirectUploadDoesOnceConfirmed()
    {
        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.State.ShouldBe(StoredFileState.Deposited);
        result.Value.Value.Category.ShouldBe(FilesSettings.Document);
        _registered.ShouldNotBeNull();
        _registered.OwnerId.ShouldBe(_callerId);
        _removed.ShouldBeNull();
    }

    [Fact]
    public async Task TheRegistrationAndTheConfirmation_AreCommittedSeparately()
    {
        var order = new List<string>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add(_registered!.State == StoredFileState.Pending ? "commit pending" : "commit deposited");

            return 1;
        });
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("write");

                return ContentWriteOutcome.Written;
            });

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        order.ShouldBe(["commit pending", "write", "commit deposited"]);
    }

    [Fact]
    public async Task TheBytes_AreWrittenFromTheStartUnderTheFilesKeyWithTheirOwnDigest()
    {
        byte[]? written = null;
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var copy = new MemoryStream();
                call.Arg<Stream>().CopyTo(copy);
                written = copy.ToArray();

                return ContentWriteOutcome.Written;
            });

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        _registered.ShouldNotBeNull();
        _registered.Checksum.Value.ShouldBe(_checksum);
        written.ShouldBe(_bytes);
        await _content.Received(1).WriteAsync(
            _registered.ObjectKey.Value,
            Arg.Any<Stream>(),
            _mediaType,
            _bytes.Length,
            _checksum,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheBytes_AreWrittenOutsideTheQuotaLock()
    {
        bool heldWhileWriting = true;
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                heldWhileWriting = _quotaLock.IsHeld;

                return ContentWriteOutcome.Written;
            });

        await UseCase().ExecuteAsync(ACommand(), TestToken);

        _quotaLock.Calls.ShouldBe(1);
        heldWhileWriting.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefusedAndNothingIsWritten()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ACommand(), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefusedOnItsField()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Category = "videos" }, TestToken);

        ShouldBeRefusedOn(result, "category");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AMediaTypeTheCategoryRefuses_IsRefusedOnTheFile()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { MediaType = "image/png" }, TestToken);

        ShouldBeRefusedOn(result, "file");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AFileAboveTheCategoryMaximum_IsRefusedOnTheFile()
    {
        _files.Categories[FilesSettings.Document].MaxSizeInBytes = _bytes.Length - 1;

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        ShouldBeRefusedOn(result, "file");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AFileAboveTheApiCeiling_IsRefusedOnTheFile()
    {
        _files.ApiUploadMaxBytes = _bytes.Length - 1;

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        ShouldBeRefusedOn(result, "file");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AContentOfAnotherLengthThanAnnounced_IsRefusedOnTheFile()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { SizeInBytes = _bytes.Length + 1 }, TestToken);

        ShouldBeRefusedOn(result, "file");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task ABlankName_IsRefusedOnItsField()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Name = " " }, TestToken);

        ShouldBeRefusedOn(result, "name");
    }

    [Fact]
    public async Task ANameWithAPathSeparator_IsRefusedByTheDomainAndNothingIsWritten()
    {
        var result = await UseCase().ExecuteAsync(ACommand() with { Name = "../report.csv" }, TestToken);

        result.Error!.Code.ShouldBe("domain.invariantViolated");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task ACategoryWithoutApiUploads_Answers409AndWritesNothing()
    {
        _files.Categories[FilesSettings.Document].UploadModes.Remove(FileCategoryOptions.ApiUploadMode);

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("storedFile.apiUploadUnavailable");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AnOwnerOverQuota_IsRefusedAndNoByteIsWritten()
    {
        _queries.GetUsageForOwnerAsync(_callerId, Arg.Any<FileCategory>(), Arg.Any<CancellationToken>())
            .Returns(new OwnerStorageUsage(0, 0, _files.Categories[FilesSettings.Document].Quota.MaxPendingUploads, 0));

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.Error!.Code.ShouldBe("storedFile.quotaExceeded");
        await ShouldHaveWrittenNothingAsync();
    }

    [Fact]
    public async Task AFailedWrite_RemovesTheRegistrationAndPropagates()
    {
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("store unreachable"));

        await Should.ThrowAsync<IOException>(() => UseCase().ExecuteAsync(ACommand(), TestToken));

        ShouldHaveRemovedTheRegistration();
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWriteTheStoreRefuses_RemovesTheRegistration()
    {
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ContentWriteOutcome.ContentMismatch);

        await Should.ThrowAsync<InvalidOperationException>(() => UseCase().ExecuteAsync(ACommand(), TestToken));

        ShouldHaveRemovedTheRegistration();
    }

    [Fact]
    public async Task AConfirmationThatFindsNothing_RemovesTheRegistrationAndReportsIt()
    {
        _content.DescribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StoredObjectDescription?)null);

        var result = await UseCase().ExecuteAsync(ACommand(), TestToken);

        result.Error!.Code.ShouldBe("storedFile.depositMissing");
        ShouldHaveRemovedTheRegistration();
    }

    [Fact]
    public async Task ACancelledWrite_StillRemovesTheRegistration()
    {
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => UseCase().ExecuteAsync(ACommand(), TestToken));

        ShouldHaveRemovedTheRegistration();
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AFailedRemoval_IsLoggedAndTheOriginalFailurePropagates()
    {
        _content.WriteAsync(
                Arg.Any<string>(),
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("store unreachable"));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => 1, _ => throw new InvalidOperationException("database unreachable"));

        await Should.ThrowAsync<IOException>(() => UseCase().ExecuteAsync(ACommand(), TestToken));

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeOfType<InvalidOperationException>();
        entry.Message.ShouldContain(_registered!.Id.ToString());
    }

    [Fact]
    public async Task ANullCommand_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    private static UploadFileCommand ACommand() =>
        new(FilesSettings.Document, "report.csv", _mediaType, _bytes.Length, new MemoryStream(_bytes));

    private static void ShouldBeRefusedOn<T>(Result<T> result, string field)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details.ShouldNotBeNull().Keys.ShouldContain(field);
    }

    private async Task ShouldHaveWrittenNothingAsync()
    {
        _repository.DidNotReceive().Add(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _content.DidNotReceive().WriteAsync(
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private void ShouldHaveRemovedTheRegistration()
    {
        _registered.ShouldNotBeNull();
        _removed.ShouldBeSameAs(_registered);
        _registered.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
    }

    private UploadFileUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private UploadFileUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new StoredFileRegistrationService(
            _repository,
            _queries,
            _quotaLock,
            _unitOfWork,
            new StubDateTimeProvider(),
            _files.AsOptions()),
        new StoredFileDepositService(_content, _unitOfWork),
        _content,
        _repository,
        _unitOfWork,
        currentUser,
        new StubDateTimeProvider(),
        _files.AsOptions(),
        new UploadFileCommandValidator(_files.AsOptions()),
        _logger);
}
