namespace AppTemplate.Application.Features.Auth.Ports.ExternalLogins;

public enum ExternalLoginLinkStatus
{
    Linked,

    /// <summary>
    /// The store would not attach the pair: no such account, or the pair is already attached to one.
    /// Not split further, since the caller answers both the same way and neither is anything the
    /// person signing in can act on.
    /// </summary>
    Refused,
}
