namespace AppTemplate.Application.Common.Idempotency;

/// <summary>What claiming an <see cref="IdempotencyKey"/> decided.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Nobody held this key; the caller may proceed and must complete or release it.</summary>
    Claimed,

    /// <summary>An identical request under the same key has not finished yet.</summary>
    InProgress,

    /// <summary>The original response is stored and may be returned as-is.</summary>
    Replay,

    /// <summary>The same key was presented with a request that hashes differently.</summary>
    KeyReused,

    /// <summary>The original response was too large to store, so it cannot be replayed.</summary>
    NotReplayable,
}
