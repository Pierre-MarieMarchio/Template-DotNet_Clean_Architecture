namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public sealed record TwoFactorSetupOutcome(
    TwoFactorSetupStatus Status,
    string? SharedKey = null,
    string? AuthenticatorUri = null)
{
    public static TwoFactorSetupOutcome AlreadyEnabled { get; } = new(TwoFactorSetupStatus.AlreadyEnabled);

    public static TwoFactorSetupOutcome NoSuchAccount { get; } = new(TwoFactorSetupStatus.NoSuchAccount);

    public static TwoFactorSetupOutcome Rejected { get; } = new(TwoFactorSetupStatus.Rejected);

    public static TwoFactorSetupOutcome Started(string sharedKey, string authenticatorUri) =>
        new(TwoFactorSetupStatus.Started, sharedKey, authenticatorUri);
}
