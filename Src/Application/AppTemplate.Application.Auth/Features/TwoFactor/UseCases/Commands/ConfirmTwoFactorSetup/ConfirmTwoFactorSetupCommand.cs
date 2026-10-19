namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>
/// <see cref="CurrentPassword"/> is required for the reason <c>ITwoFactorService.ConfirmSetupAsync</c>
/// gives: arming the second factor is as much a security-posture change as disarming it, and a
/// stolen session must not be able to make either one alone.
/// </summary>
public sealed record ConfirmTwoFactorSetupCommand(string CurrentPassword, string Code);
