namespace AppTemplate.Application.Core.Common.Idempotency;

/// <summary>The verdict <see cref="IIdempotencyStore.ClaimAsync"/> reaches for one key.</summary>
public sealed record IdempotencyClaim
{
    private IdempotencyClaim(IdempotencyStatus status, IdempotentResponse? response)
    {
        Status = status;
        Response = response;
    }

    /// <summary>Which verdict this is, and so whether the caller proceeds, replays, or is refused.</summary>
    public IdempotencyStatus Status { get; }

    /// <summary>Set only for <see cref="IdempotencyStatus.Replay"/>.</summary>
    public IdempotentResponse? Response { get; }

    /// <summary>
    /// Nobody held the key: the caller owns it, and owes the store either a completion or a release.
    /// </summary>
    public static IdempotencyClaim Claimed() => new(IdempotencyStatus.Claimed, null);

    /// <summary>
    /// An identical request holds the key and has not finished, so there is nothing to replay yet.
    /// </summary>
    public static IdempotencyClaim InProgress() => new(IdempotencyStatus.InProgress, null);

    /// <summary>
    /// The original response is stored: the caller is answered with it rather than the action running
    /// twice.
    /// </summary>
    public static IdempotencyClaim Replay(IdempotentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new(IdempotencyStatus.Replay, response);
    }

    /// <summary>
    /// The key belongs to a request that fingerprints differently, so neither running it nor replaying
    /// the other would be the answer the caller asked for.
    /// </summary>
    public static IdempotencyClaim KeyReused() => new(IdempotencyStatus.KeyReused, null);

    /// <summary>
    /// The key completed but its response was never stored, so there is nothing faithful left to replay.
    /// </summary>
    public static IdempotencyClaim NotReplayable() => new(IdempotencyStatus.NotReplayable, null);
}
