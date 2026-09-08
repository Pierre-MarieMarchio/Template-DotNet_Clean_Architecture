namespace AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;

/// <summary>How attaching a provider identity to an account that already exists ended.</summary>
public enum ExternalLoginLinkStatus
{
    /// <summary>The pair is attached, so a later sign-in with it resolves this account directly.</summary>
    Linked,

    /// <summary>
    /// The store would not attach the pair: no such account, or the pair is already attached to one.
    /// Not split further, since the caller answers both the same way and neither is anything the
    /// person signing in can act on.
    /// </summary>
    Refused,
}
