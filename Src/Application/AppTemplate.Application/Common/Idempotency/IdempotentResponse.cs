namespace AppTemplate.Application.Common.Idempotency;

/// <summary>
/// The response a claimed request produced, stored so a retry under the same key can be answered
/// identically instead of re-running the action.
/// </summary>
/// <param name="Body">
/// The JSON of the executed result's value, or <c>null</c> when it was too large to store — in
/// which case a replay is answered with <see cref="IdempotencyErrors.NotReplayable"/> instead of a
/// truncated body.
/// </param>
/// <param name="ETag">
/// The version the original response published, if any. Without this a replay of a create or
/// update would hand the caller back a body carrying no validator at all, leaving it unable to
/// make the very conditional request the <c>ETag</c> exists to support.
/// </param>
public sealed record IdempotentResponse(int StatusCode, string? Body, string? Location, string? ETag = null);
