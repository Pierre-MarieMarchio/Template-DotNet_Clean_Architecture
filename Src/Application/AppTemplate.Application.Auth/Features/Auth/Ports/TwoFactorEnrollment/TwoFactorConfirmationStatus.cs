namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;

/// <summary>How confirming a pending enrollment with a first code ended.</summary>
public enum TwoFactorConfirmationStatus
{
    /// <summary>Two-factor sign-in is on and a fresh set of recovery codes exists.</summary>
    Confirmed,

    /// <summary>
    /// The code did not match — including when no pending secret exists at all, which
    /// <c>BeginAsync</c> should have been called to provision first.
    /// </summary>
    InvalidCode,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectPassword,
}
