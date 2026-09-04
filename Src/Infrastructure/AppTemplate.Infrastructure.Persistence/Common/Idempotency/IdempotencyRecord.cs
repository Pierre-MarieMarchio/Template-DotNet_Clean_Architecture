namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

/// <summary>
/// One claimed <c>Idempotency-Key</c>, keyed by the user that presented it and the key itself.
/// Internal: reached only through <see cref="IdempotencyStore"/>, behind
/// <see cref="Application.Common.Idempotency.IIdempotencyStore"/>.
/// </summary>
internal sealed class IdempotencyRecord
{
    public required Guid UserId { get; set; }

    public required string Key { get; set; }

    public required string Endpoint { get; set; }

    public required string Fingerprint { get; set; }

    /// <summary>Set once the action has run and <see cref="IdempotencyStore.CompleteAsync"/> was called.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Only set once <see cref="IsCompleted"/> is true.</summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Only set once <see cref="IsCompleted"/> is true, and even then only when the response was
    /// small enough to keep — <c>null</c> here is what makes a later claim answer
    /// <c>idempotency.notReplayable</c> instead of replaying a truncated body.
    /// </summary>
    public string? ResponseBody { get; set; }

    public string? Location { get; set; }

    /// <summary>
    /// Only set once <see cref="IsCompleted"/> is true, and even then only when the response
    /// published a validator — a write that publishes none, a 204, has no <c>ETag</c> to keep. A
    /// replay read back from here is the only way a retry answered by another instance can still
    /// carry the validator the original response did.
    /// </summary>
    public string? ETag { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}
