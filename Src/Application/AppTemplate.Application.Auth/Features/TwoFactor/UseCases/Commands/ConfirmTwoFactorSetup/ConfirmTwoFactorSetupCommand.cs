namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;

public sealed record ConfirmTwoFactorSetupCommand(string CurrentPassword, string Code);
