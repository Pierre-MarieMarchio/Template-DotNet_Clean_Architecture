namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

public enum TwoFactorSetupStatus
{
    Started,

    /// <summary>
    /// Two-factor sign-in is already active. Provisioning a second secret on top of a live one would
    /// hand back a key none of the account's existing authenticator apps were built from, with no
    /// warning that the old one is about to stop being checked.
    /// </summary>
    AlreadyEnabled,
}
