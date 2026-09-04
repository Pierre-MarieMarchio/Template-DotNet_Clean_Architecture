namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>Carries no identity: the pending secret is the authenticated caller's own.</summary>
public sealed record ConfirmTwoFactorSetupRequest(string Code);
