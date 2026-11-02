namespace AppTemplate.Application.Core.Common.Idempotency;

public sealed record IdempotentResponse(int StatusCode, string? Body, string? Location, string? ETag = null);
