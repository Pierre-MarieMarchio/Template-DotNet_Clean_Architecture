namespace AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;

public sealed record TwoFactorConfirmationOutcome(
    TwoFactorConfirmationStatus Status,
    IReadOnlyList<string>? RecoveryCodes = null)
{
    public static TwoFactorConfirmationOutcome InvalidCode { get; } = new(TwoFactorConfirmationStatus.InvalidCode);

    public static TwoFactorConfirmationOutcome IncorrectPassword { get; } =
        new(TwoFactorConfirmationStatus.IncorrectPassword);

    public static TwoFactorConfirmationOutcome NoSuchAccount { get; } = new(TwoFactorConfirmationStatus.NoSuchAccount);

    public static TwoFactorConfirmationOutcome AlreadyEnabled { get; } = new(TwoFactorConfirmationStatus.AlreadyEnabled);

    public static TwoFactorConfirmationOutcome Rejected { get; } = new(TwoFactorConfirmationStatus.Rejected);

    public static TwoFactorConfirmationOutcome Confirmed(IReadOnlyList<string> recoveryCodes) =>
        new(TwoFactorConfirmationStatus.Confirmed, recoveryCodes);
}
