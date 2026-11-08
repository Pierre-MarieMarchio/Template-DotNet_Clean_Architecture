namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;

public sealed record ConfirmTwoFactorSetupOutcome(IReadOnlyList<string> RecoveryCodes);
