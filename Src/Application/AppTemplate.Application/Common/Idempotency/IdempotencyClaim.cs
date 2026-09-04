namespace AppTemplate.Application.Common.Idempotency;

/// <summary>The verdict <see cref="IIdempotencyStore.ClaimAsync"/> reaches for one key.</summary>
public sealed record IdempotencyClaim
{
    private IdempotencyClaim(IdempotencyOutcome outcome, IdempotentResponse? response)
    {
        Outcome = outcome;
        Response = response;
    }

    public IdempotencyOutcome Outcome { get; }

    /// <summary>Set only for <see cref="IdempotencyOutcome.Replay"/>.</summary>
    public IdempotentResponse? Response { get; }

    public static IdempotencyClaim Claimed() => new(IdempotencyOutcome.Claimed, null);

    public static IdempotencyClaim InProgress() => new(IdempotencyOutcome.InProgress, null);

    public static IdempotencyClaim Replay(IdempotentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new(IdempotencyOutcome.Replay, response);
    }

    public static IdempotencyClaim KeyReused() => new(IdempotencyOutcome.KeyReused, null);

    public static IdempotencyClaim NotReplayable() => new(IdempotencyOutcome.NotReplayable, null);
}
