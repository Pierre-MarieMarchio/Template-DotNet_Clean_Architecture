namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

/// <param name="RecoveryCodes">
/// Ten single-use codes, generated the moment two-factor sign-in actually turns on — not at
/// <c>BeginAsync</c>, where enrollment might never be confirmed at all. Shown once; the store never
/// hands the plain codes back after this. Set only for <see cref="TwoFactorConfirmationStatus.Confirmed"/>.
/// </param>
public sealed record TwoFactorConfirmationOutcome(
    TwoFactorConfirmationStatus Status,
    IReadOnlyList<string>? RecoveryCodes = null)
{
    public static TwoFactorConfirmationOutcome InvalidCode { get; } = new(TwoFactorConfirmationStatus.InvalidCode);

    public static TwoFactorConfirmationOutcome IncorrectPassword { get; } =
        new(TwoFactorConfirmationStatus.IncorrectPassword);

    public static TwoFactorConfirmationOutcome Confirmed(IReadOnlyList<string> recoveryCodes) =>
        new(TwoFactorConfirmationStatus.Confirmed, recoveryCodes);
}
