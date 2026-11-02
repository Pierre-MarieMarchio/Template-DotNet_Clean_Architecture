namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.SetUpTwoFactor;

public sealed record SetUpTwoFactorOutcome(string SharedKey, string AuthenticatorUri);
