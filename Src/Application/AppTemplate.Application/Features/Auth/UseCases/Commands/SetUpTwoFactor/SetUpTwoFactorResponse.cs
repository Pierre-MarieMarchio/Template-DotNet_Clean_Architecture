namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;

public sealed record SetUpTwoFactorResponse(string SharedKey, string AuthenticatorUri);
