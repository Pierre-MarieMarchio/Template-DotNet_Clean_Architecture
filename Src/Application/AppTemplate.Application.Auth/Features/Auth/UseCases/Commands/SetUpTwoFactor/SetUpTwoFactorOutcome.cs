namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SetUpTwoFactor;

/// <summary>The provisioned secret, in the two forms an authenticator app accepts.</summary>
/// <param name="SharedKey">Base32, for typing in by hand.</param>
/// <param name="AuthenticatorUri">The same secret as an <c>otpauth://</c> URI, for a QR code.</param>
public sealed record SetUpTwoFactorOutcome(string SharedKey, string AuthenticatorUri);
