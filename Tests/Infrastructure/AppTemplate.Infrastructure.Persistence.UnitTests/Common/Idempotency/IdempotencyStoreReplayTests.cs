using AppTemplate.Application.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Idempotency;

/// <summary>
/// <see cref="IdempotencyStore.Decide"/> is what a retry gets handed back once the row it collides
/// with has been read: the point where stored columns become an <see cref="IdempotentResponse"/>
/// again. A column dropped here is silent — the replay simply arrives without it — so the validator
/// is asserted explicitly.
/// </summary>
/// <remarks>
/// No database. The row is built directly, which is exactly what a read from PostgreSQL produces;
/// that the <c>ETag</c> column survives a real round trip is covered in
/// <c>AppTemplate.Api.IntegrationTests</c>.
/// </remarks>
public sealed class IdempotencyStoreReplayTests
{
    private const string _fingerprint = "9f2c1b7d4e6a8c05f3b9d1e7a2c4680f5d3b9e1a7c2f48609d5b3e1a7c2f4860";

    [Fact]
    public void Decide_ReplaysTheStoredETag_Unchanged()
    {
        IdempotencyClaim claim = IdempotencyStore.Decide(AKey(), ACompletedRecord(eTag: "\"AAAwOQ\""));

        claim.Status.ShouldBe(IdempotencyStatus.Replay);
        claim.Response!.ETag.ShouldBe(
            "\"AAAwOQ\"",
            "a replayed representation is the same representation, so it carries the same validator");
    }

    /// <summary>
    /// A write that published no validator must replay as having none. An empty string would be a
    /// syntactically valid <c>ETag</c> header value that matches nothing, which is worse than absent:
    /// the caller would send it back in an <c>If-Match</c> that can only ever fail.
    /// </summary>
    [Fact]
    public void Decide_ReplaysNoETag_WhenTheOriginalResponsePublishedNone()
    {
        IdempotencyClaim claim = IdempotencyStore.Decide(AKey(), ACompletedRecord(eTag: null));

        claim.Status.ShouldBe(IdempotencyStatus.Replay);
        claim.Response!.ETag.ShouldBeNull();
    }

    [Fact]
    public void Decide_ReplaysTheStatusCodeBodyAndLocation_AlongsideTheValidator()
    {
        IdempotencyClaim claim = IdempotencyStore.Decide(AKey(), ACompletedRecord(eTag: "\"AAAwOQ\""));

        IdempotentResponse response = claim.Response!;
        response.StatusCode.ShouldBe(201);
        response.Body.ShouldBe("""{"id":"list"}""");
        response.Location.ShouldBe("/api/v1/todo-lists/list");
    }

    [Fact]
    public void Decide_RefusesToReplay_WhenTheKeyWasPresentedWithADifferentRequest()
    {
        IdempotencyRecord record = ACompletedRecord(eTag: "\"AAAwOQ\"");
        record.Fingerprint = new string('0', 64);

        IdempotencyStore.Decide(AKey(), record).Status.ShouldBe(IdempotencyStatus.KeyReused);
    }

    [Fact]
    public void Decide_ReportsTheClaimStillRunning_WhenTheRowIsNotCompleted()
    {
        IdempotencyRecord record = ACompletedRecord(eTag: null);
        record.IsCompleted = false;

        IdempotencyStore.Decide(AKey(), record).Status.ShouldBe(IdempotencyStatus.InProgress);
    }

    /// <summary>A dropped body cannot be replayed even when the validator survived it.</summary>
    [Fact]
    public void Decide_RefusesToReplay_WhenTheBodyWasTooLargeToStore()
    {
        IdempotencyRecord record = ACompletedRecord(eTag: "\"AAAwOQ\"");
        record.ResponseBody = null;

        IdempotencyStore.Decide(AKey(), record).Status.ShouldBe(IdempotencyStatus.NotReplayable);
    }

    private static IdempotencyKey AKey() =>
        IdempotencyKey.Create(
            userId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            key: "a-key",
            endpoint: "POST /api/v1/todo-lists",
            fingerprint: _fingerprint,
            maxKeyLength: 512).Value;

    private static IdempotencyRecord ACompletedRecord(string? eTag) => new()
    {
        UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Key = "a-key",
        Endpoint = "POST /api/v1/todo-lists",
        Fingerprint = _fingerprint,
        IsCompleted = true,
        StatusCode = 201,
        ResponseBody = """{"id":"list"}""",
        Location = "/api/v1/todo-lists/list",
        ETag = eTag,
        CreatedAt = DateTimeOffset.UnixEpoch,
        ClaimedUntil = DateTimeOffset.UnixEpoch.AddMinutes(15),
        ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(1),
    };
}
