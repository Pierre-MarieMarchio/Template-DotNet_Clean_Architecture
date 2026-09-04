namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;

public sealed record SetUpTwoFactorOutcome(string SharedKey, string AuthenticatorUri);
