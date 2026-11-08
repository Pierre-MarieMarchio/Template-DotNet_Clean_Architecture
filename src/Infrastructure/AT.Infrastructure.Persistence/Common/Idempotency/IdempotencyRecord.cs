namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

internal sealed class IdempotencyRecord
{
    public required Guid UserId { get; set; }

    public required string Key { get; set; }

    public required string Endpoint { get; set; }

    public required string Fingerprint { get; set; }

    public bool IsCompleted { get; set; }

    public required DateTimeOffset ClaimedUntil { get; set; }

    public int? StatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public string? Location { get; set; }

    public string? ETag { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}
