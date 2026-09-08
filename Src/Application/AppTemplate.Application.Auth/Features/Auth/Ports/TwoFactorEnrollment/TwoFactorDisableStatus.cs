namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;

/// <summary>How turning an account's own second factor off ended.</summary>
public enum TwoFactorDisableStatus
{
    /// <summary>The account no longer demands a code at sign-in.</summary>
    Disabled,

    /// <summary>The supplied current password did not match the one on file.</summary>
    IncorrectPassword,
}
