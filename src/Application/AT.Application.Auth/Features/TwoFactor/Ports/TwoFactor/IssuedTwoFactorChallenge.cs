namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public sealed record IssuedTwoFactorChallenge(string ChallengeToken, DateTimeOffset ExpiresAt);
