using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Idempotency;

/// <summary>The catalogue of failures the idempotency filter can produce, wherever it is wired in.</summary>
public static class IdempotencyErrors
{
    /// <summary>
    /// The header itself is unusable — blank, or past the configured length — so no claim is attempted.
    /// </summary>
    public static Error KeyInvalid(string message) => Error.Validation("idempotency.keyInvalid", message);

    /// <summary>Answers <see cref="IdempotencyStatus.KeyReused"/>.</summary>
    public static readonly Error KeyReused = Error.Conflict(
        "idempotency.keyReused",
        "This 'Idempotency-Key' was already used with a request that had a different method, path or body.");

    /// <summary>
    /// Answers <see cref="IdempotencyStatus.InProgress"/>. Worth retrying, which is why the message names
    /// a delay rather than a fault.
    /// </summary>
    public static readonly Error InProgress = Error.Conflict(
        "idempotency.inProgress",
        "A request with this 'Idempotency-Key' is still being processed. Retry shortly.");

    /// <summary>
    /// Answers <see cref="IdempotencyStatus.NotReplayable"/>: refused outright rather than served a
    /// partial body a client would take for the original.
    /// </summary>
    public static readonly Error NotReplayable = Error.Conflict(
        "idempotency.notReplayable",
        "The original response for this 'Idempotency-Key' was too large to store; it cannot be replayed.");
}
