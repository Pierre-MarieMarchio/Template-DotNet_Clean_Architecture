namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;

/// <param name="RecoveryCodes">
/// Ten single-use codes, generated the moment two-factor sign-in actually turns on — not at
/// <c>BeginAsync</c>, where enrollment might never be confirmed at all. Shown once; the store never
/// hands the plain codes back after this. Set only for <see cref="TwoFactorConfirmationStatus.Confirmed"/>.
/// </param>
public sealed record TwoFactorConfirmationOutcome(
    TwoFactorConfirmationStatus Status,
    IReadOnlyList<string>? RecoveryCodes = null)
{
    /// <summary>The code did not match the pending secret, so the second factor stays off.</summary>
    public static TwoFactorConfirmationOutcome InvalidCode { get; } = new(TwoFactorConfirmationStatus.InvalidCode);

    /// <summary>The password did not match: nothing armed, and no recovery codes minted.</summary>
    public static TwoFactorConfirmationOutcome IncorrectPassword { get; } =
        new(TwoFactorConfirmationStatus.IncorrectPassword);

    /// <summary>Two-factor sign-in is armed. The codes travel here because nothing reads them back.</summary>
    public static TwoFactorConfirmationOutcome Confirmed(IReadOnlyList<string> recoveryCodes) =>
        new(TwoFactorConfirmationStatus.Confirmed, recoveryCodes);
}
