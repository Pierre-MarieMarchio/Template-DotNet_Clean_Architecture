namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>The passkeys registered on the caller's account, the newest first.</summary>
public sealed record PasskeysResponse(IReadOnlyList<PasskeyResponse> Passkeys);
