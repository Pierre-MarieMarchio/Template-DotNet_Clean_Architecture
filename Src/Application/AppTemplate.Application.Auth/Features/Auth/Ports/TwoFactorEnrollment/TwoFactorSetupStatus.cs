namespace AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;

/// <summary>How provisioning a secret for enrollment ended.</summary>
public enum TwoFactorSetupStatus
{
    /// <summary>A secret is pending. It arms nothing on its own: only a confirmed code does that.</summary>
    Started,

    /// <summary>
    /// Two-factor sign-in is already active. Provisioning a second secret on top of a live one would
    /// hand back a key none of the account's existing authenticator apps were built from, with no
    /// warning that the old one is about to stop being checked.
    /// </summary>
    AlreadyEnabled,
}
