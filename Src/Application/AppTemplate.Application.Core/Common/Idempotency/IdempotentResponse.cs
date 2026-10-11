namespace AppTemplate.Application.Core.Common.Idempotency;

/// <summary>The response a claimed request produced, stored so a retry under the same key can be answered identically instead of re-running the action.</summary>
/// <param name="Body">The JSON of the executed result's value, or <c>null</c> when it was too large
/// to store — then a replay is answered with <see cref="IdempotencyErrors.NotReplayable"/> rather
/// than a truncated body. A 204 stores <c>null</c> too, but replays as it was.</param>
/// <param name="ETag">The version the original response published, if any — without it a replay of a
/// create or update would hand back a body carrying no validator for the conditional request the
/// <c>ETag</c> exists to support.</param>
public sealed record IdempotentResponse(int StatusCode, string? Body, string? Location, string? ETag = null);
