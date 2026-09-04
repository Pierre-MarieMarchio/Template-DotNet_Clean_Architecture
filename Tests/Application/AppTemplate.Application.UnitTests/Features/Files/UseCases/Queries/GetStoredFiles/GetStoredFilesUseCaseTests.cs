using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.GetStoredFiles;

public sealed class GetStoredFilesUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();

    public GetStoredFilesUseCaseTests() =>
        _queries.GetForOwnerAsync(
                Arg.Any<Guid>(),
                Arg.Any<StoredFilePageRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<StoredFileDto>([], 1, 20, 0));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(GetStoredFilesQuery.Offset(1, 20), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(GetStoredFilesQuery.Offset(1, 20), TestToken);

        await _queries.DidNotReceive().GetForOwnerAsync(
            Arg.Any<Guid>(), Arg.Any<StoredFilePageRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The owner comes from the caller and is not part of the query, so there is no parameter a
    /// caller could widen. Passing anything but the caller's own id here would list somebody else's
    /// files.
    /// </summary>
    [Fact]
    public async Task ThePage_IsAlwaysScopedToTheCaller()
    {
        await UseCase().ExecuteAsync(GetStoredFilesQuery.Offset(1, 20), TestToken);

        await _queries.Received(1).GetForOwnerAsync(
            _callerId, Arg.Any<StoredFilePageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnwhitelistedSortField_IsRefusedBeforeAnythingIsQueried()
    {
        var query = new GetStoredFilesQuery(null, null, null, null, "objectKey:asc", null, null);

        var result = await UseCase().ExecuteAsync(query, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
        await _queries.DidNotReceive().GetForOwnerAsync(
            Arg.Any<Guid>(), Arg.Any<StoredFilePageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APageSizeAboveTheCeiling_IsRefused()
    {
        var result = await UseCase().ExecuteAsync(
            GetStoredFilesQuery.Offset(1, StoredFileCollectionPolicy.Instance.MaxPageSize + 1),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public async Task AnUnrecognisedStateFilter_IsRefused()
    {
        var query = new GetStoredFilesQuery(null, null, null, null, null, null, "deleted");

        var result = await UseCase().ExecuteAsync(query, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public async Task ARecognisedStateFilter_ReachesThePort()
    {
        var query = new GetStoredFilesQuery(null, null, null, null, null, null, "pending");

        await UseCase().ExecuteAsync(query, TestToken);

        await _queries.Received(1).GetForOwnerAsync(
            _callerId,
            Arg.Is<StoredFilePageRequest>(request =>
                request != null && request.Filter.State == StoredFileState.Pending),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A cursor sent with <c>paging=offset</c> must fail rather than be quietly ignored: ignoring it
    /// would serve page one to a caller who asked to resume, and they would never notice.
    /// </summary>
    [Fact]
    public async Task ACursorSentInOffsetMode_IsRefused()
    {
        string cursor = Cursor
            .After(
                SortOrder.Parse("registeredAt:desc", StoredFileCollectionPolicy.Instance).Value.Terms[0],
                StubDateTimeProvider.DefaultInstant.ToString("O", CultureInfo.InvariantCulture),
                Guid.CreateVersion7())
            .Encode();

        var query = new GetStoredFilesQuery("offset", null, null, cursor, null, null, null);

        var result = await UseCase().ExecuteAsync(query, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    /// <summary>
    /// A cursor minted under one order and replayed under another compares a value against a column
    /// it does not match — a file name parsed as a date — which the persistence layer could only
    /// answer by throwing. Refused here, it is a 400 like every other malformed request.
    /// </summary>
    [Fact]
    public async Task ACursorReplayedUnderADifferentSort_IsRefused()
    {
        string cursor = Cursor
            .After(
                SortOrder.Parse("name:asc", StoredFileCollectionPolicy.Instance).Value.Terms[0],
                "holiday.png",
                Guid.CreateVersion7())
            .Encode();

        var query = new GetStoredFilesQuery("cursor", null, null, cursor, "registeredAt:desc", null, null);

        var result = await UseCase().ExecuteAsync(query, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    /// <summary>
    /// A tampered date key would otherwise be discovered by the keyset predicate, whose only
    /// recourse is to throw — a 500 for a malformed request.
    /// </summary>
    [Fact]
    public async Task ACursorWhoseKeyIsNotADateForADateField_IsRefused()
    {
        string cursor = Cursor
            .After(
                SortOrder.Parse("registeredAt:desc", StoredFileCollectionPolicy.Instance).Value.Terms[0],
                "not-a-date",
                Guid.CreateVersion7())
            .Encode();

        var query = new GetStoredFilesQuery("cursor", null, null, cursor, "registeredAt:desc", null, null);

        var result = await UseCase().ExecuteAsync(query, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public async Task AWellFormedQuery_ReturnsThePage()
    {
        var result = await UseCase().ExecuteAsync(GetStoredFilesQuery.Offset(1, 20), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task ANullQuery_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    private GetStoredFilesUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private GetStoredFilesUseCase UseCaseFor(ICurrentUser currentUser) => new(_queries, currentUser);
}
