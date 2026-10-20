namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;

public sealed record VerifyTwoFactorCommand(string ChallengeToken, string Code);
