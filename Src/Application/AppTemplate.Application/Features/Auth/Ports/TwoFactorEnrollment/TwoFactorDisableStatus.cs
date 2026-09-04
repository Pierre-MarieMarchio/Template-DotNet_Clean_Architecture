namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

public enum TwoFactorDisableStatus
{
    Disabled,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectPassword,
}
