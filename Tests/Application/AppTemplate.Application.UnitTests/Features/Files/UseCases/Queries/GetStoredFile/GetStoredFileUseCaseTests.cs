using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.GetStoredFile;

public sealed class GetStoredFileUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetStoredFileQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new GetStoredFileQuery(Guid.CreateVersion7()), TestToken);

        await _queries.DidNotReceive().GetDetailAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ownership filter is passed into the query rather than applied to its result. Reading the
    /// row first and comparing afterwards would work today and would be one careless edit away from
    /// serving a file to whoever asked for it by id.
    /// </summary>
    [Fact]
    public async Task TheOwnerFilter_IsTheCallerAndReachesThePort()
    {
        var fileId = Guid.CreateVersion7();
        _queries.GetDetailAsync(fileId, _callerId, Arg.Any<CancellationToken>()).Returns(ADetail(fileId));

        await UseCase().ExecuteAsync(new GetStoredFileQuery(fileId), TestToken);

        await _queries.Received(1).GetDetailAsync(fileId, _callerId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Somebody else's file answers exactly as a missing one does, because the port answers
    /// <c>null</c> for both and this use case cannot tell them apart even if it wanted to.
    /// </summary>
    [Fact]
    public async Task AFileThatIsNotTheCallers_IsReportedAsNotFound()
    {
        var fileId = Guid.CreateVersion7();
        _queries.GetDetailAsync(fileId, _callerId, Arg.Any<CancellationToken>())
            .Returns((Versioned<StoredFileDto>?)null);

        var result = await UseCase().ExecuteAsync(new GetStoredFileQuery(fileId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("storedFile.notFound");
    }

    [Fact]
    public async Task TheCallersOwnFile_IsReturnedWithItsVersion()
    {
        var fileId = Guid.CreateVersion7();
        _queries.GetDetailAsync(fileId, _callerId, Arg.Any<CancellationToken>()).Returns(ADetail(fileId));

        var result = await UseCase().ExecuteAsync(new GetStoredFileQuery(fileId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Id.ShouldBe(fileId);
        result.Value.Version.ShouldBe(3u);
    }

    /// <summary>Metadata only. A URL here would be a bearer right handed out on every read.</summary>
    [Fact]
    public void TheReadModel_CarriesNoUrlAndNoContent() =>
        typeof(StoredFileDto)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldBe(
                [
                    nameof(StoredFileDto.Id),
                    nameof(StoredFileDto.Name),
                    nameof(StoredFileDto.DeclaredMediaType),
                    nameof(StoredFileDto.SizeInBytes),
                    nameof(StoredFileDto.Checksum),
                    nameof(StoredFileDto.State),
                    nameof(StoredFileDto.RegisteredAt),
                    nameof(StoredFileDto.AvailableAt),
                ],
                ignoreOrder: true);

    [Fact]
    public async Task AnEmptyId_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(new GetStoredFileQuery(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ANullQuery_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    private static Versioned<StoredFileDto> ADetail(Guid fileId) => new(
        new StoredFileDto(
            fileId,
            "holiday.png",
            AStoredFile.MediaType,
            AStoredFile.SizeInBytes,
            AStoredFile.Checksum,
            StoredFileState.Available,
            StubDateTimeProvider.DefaultInstant,
            StubDateTimeProvider.DefaultInstant),
        3u);

    private GetStoredFileUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private GetStoredFileUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser, new GetStoredFileQueryValidator());
}
