namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

/// <param name="SharedKey">
/// The raw secret, base32-encoded for an authenticator app to type in by hand. Set only for
/// <see cref="TwoFactorSetupStatus.Started"/>.
/// </param>
/// <param name="AuthenticatorUri">
/// The same secret as an <c>otpauth://</c> URI, for a QR code. Set only alongside
/// <paramref name="SharedKey"/>.
/// </param>
public sealed record TwoFactorSetupOutcome(
    TwoFactorSetupStatus Status,
    string? SharedKey = null,
    string? AuthenticatorUri = null)
{
    public static TwoFactorSetupOutcome AlreadyEnabled { get; } = new(TwoFactorSetupStatus.AlreadyEnabled);

    public static TwoFactorSetupOutcome Started(string sharedKey, string authenticatorUri) =>
        new(TwoFactorSetupStatus.Started, sharedKey, authenticatorUri);
}
