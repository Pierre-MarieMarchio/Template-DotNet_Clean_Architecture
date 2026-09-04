namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

/// <param name="RecoveryCodes">
/// Ten single-use codes, generated the moment two-factor sign-in actually turns on — not at
/// <c>BeginAsync</c>, where enrollment might never be confirmed at all. Shown once; the store never
/// hands the plain codes back after this. Set only for <see cref="TwoFactorConfirmationOutcome.Confirmed"/>.
/// </param>
public sealed record TwoFactorConfirmation(
    TwoFactorConfirmationOutcome Outcome,
    IReadOnlyList<string>? RecoveryCodes = null)
{
    public static TwoFactorConfirmation InvalidCode { get; } = new(TwoFactorConfirmationOutcome.InvalidCode);

    public static TwoFactorConfirmation Confirmed(IReadOnlyList<string> recoveryCodes) =>
        new(TwoFactorConfirmationOutcome.Confirmed, recoveryCodes);
}
