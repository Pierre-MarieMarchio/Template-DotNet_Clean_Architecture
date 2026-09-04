namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <param name="RecoveryCodes">
/// Shown once and never retrievable again. Losing them along with the authenticator app is losing
/// the account.
/// </param>
public sealed record ConfirmTwoFactorSetupResponse(IReadOnlyList<string> RecoveryCodes);
