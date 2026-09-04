namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

/// <param name="RecoveryCodes">
/// Shown once and never retrievable again: the store never hands the plain codes back after this
/// response. Losing them along with the authenticator app is losing the account.
/// </param>
public sealed record ConfirmTwoFactorSetupResponse(IReadOnlyList<string> RecoveryCodes);
