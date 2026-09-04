namespace AppTemplate.Application.Common.Idempotency;

/// <summary>The verdict <see cref="IIdempotencyStore.ClaimAsync"/> reaches for one key.</summary>
public sealed record IdempotencyClaim
{
    private IdempotencyClaim(IdempotencyStatus status, IdempotentResponse? response)
    {
        Status = status;
        Response = response;
    }

    public IdempotencyStatus Status { get; }

    /// <summary>Set only for <see cref="IdempotencyStatus.Replay"/>.</summary>
    public IdempotentResponse? Response { get; }

    public static IdempotencyClaim Claimed() => new(IdempotencyStatus.Claimed, null);

    public static IdempotencyClaim InProgress() => new(IdempotencyStatus.InProgress, null);

    public static IdempotencyClaim Replay(IdempotentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new(IdempotencyStatus.Replay, response);
    }

    public static IdempotencyClaim KeyReused() => new(IdempotencyStatus.KeyReused, null);

    public static IdempotencyClaim NotReplayable() => new(IdempotencyStatus.NotReplayable, null);
}
