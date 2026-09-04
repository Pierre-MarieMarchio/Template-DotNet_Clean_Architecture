using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Common.Idempotency;

/// <summary>The catalogue of failures the idempotency filter can produce, wherever it is wired in.</summary>
public static class IdempotencyErrors
{
    public static Error KeyInvalid(string message) => Error.Validation("idempotency.keyInvalid", message);

    public static readonly Error KeyReused = Error.Conflict(
        "idempotency.keyReused",
        "This 'Idempotency-Key' was already used with a request that had a different method, path or body.");

    public static readonly Error InProgress = Error.Conflict(
        "idempotency.inProgress",
        "A request with this 'Idempotency-Key' is still being processed. Retry shortly.");

    public static readonly Error NotReplayable = Error.Conflict(
        "idempotency.notReplayable",
        "The original response for this 'Idempotency-Key' was too large to store; it cannot be replayed.");
}
