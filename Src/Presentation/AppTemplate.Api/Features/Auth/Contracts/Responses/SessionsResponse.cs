namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>The caller's live sessions, most recently used first.</summary>
public sealed record SessionsResponse(IReadOnlyList<SessionResponse> Sessions);
