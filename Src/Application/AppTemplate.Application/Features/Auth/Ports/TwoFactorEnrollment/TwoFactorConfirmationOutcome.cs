namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

public enum TwoFactorConfirmationOutcome
{
    Confirmed,

    /// <summary>
    /// The code did not match — including when no pending secret exists at all, which
    /// <c>BeginAsync</c> should have been called to provision first.
    /// </summary>
    InvalidCode,
}
