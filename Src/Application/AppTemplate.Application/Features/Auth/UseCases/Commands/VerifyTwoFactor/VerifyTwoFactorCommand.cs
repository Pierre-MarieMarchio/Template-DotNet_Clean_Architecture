namespace AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;

/// <param name="Code">Either the authenticator app's current six digits or one of the recovery codes.</param>
public sealed record VerifyTwoFactorCommand(string ChallengeToken, string Code);
