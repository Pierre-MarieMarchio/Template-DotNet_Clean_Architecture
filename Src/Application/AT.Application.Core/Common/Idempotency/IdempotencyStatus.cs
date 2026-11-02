namespace AppTemplate.Application.Core.Common.Idempotency;

public enum IdempotencyStatus
{
    Claimed,

    InProgress,

    Replay,

    KeyReused,

    NotReplayable,
}
