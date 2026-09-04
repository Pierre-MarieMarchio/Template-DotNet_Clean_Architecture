namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

/// <param name="SharedKey">
/// The raw secret, base32-encoded for an authenticator app to type in by hand. Set only for
/// <see cref="TwoFactorSetupOutcome.Started"/>.
/// </param>
/// <param name="AuthenticatorUri">
/// The same secret as an <c>otpauth://</c> URI, for a QR code. Set only alongside
/// <paramref name="SharedKey"/>.
/// </param>
public sealed record TwoFactorSetup(
    TwoFactorSetupOutcome Outcome,
    string? SharedKey = null,
    string? AuthenticatorUri = null)
{
    public static TwoFactorSetup AlreadyEnabled { get; } = new(TwoFactorSetupOutcome.AlreadyEnabled);

    public static TwoFactorSetup Started(string sharedKey, string authenticatorUri) =>
        new(TwoFactorSetupOutcome.Started, sharedKey, authenticatorUri);
}
