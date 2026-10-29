namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>The caller's live access keys, the newest first.</summary>
public sealed record AccessKeysResponse(IReadOnlyList<AccessKeyResponse> AccessKeys);
