namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <param name="SharedKey">The raw secret, for typing into an authenticator app by hand.</param>
/// <param name="AuthenticatorUri">The same secret as an <c>otpauth://</c> URI, for a QR code.</param>
public sealed record SetUpTwoFactorResponse(string SharedKey, string AuthenticatorUri);
