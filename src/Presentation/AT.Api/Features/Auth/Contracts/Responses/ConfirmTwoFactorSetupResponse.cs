namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <param name="RecoveryCodes">Shown once and never retrievable again.</param>
public sealed record ConfirmTwoFactorSetupResponse(IReadOnlyList<string> RecoveryCodes);
