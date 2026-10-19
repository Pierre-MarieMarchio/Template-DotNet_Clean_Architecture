using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.Model.Policies;

/// <summary>Whether a provider identity signing in for the first time may take over a local
/// account, given what that address already points at. A pure function of one input, kept out of the
/// use case on purpose: it is the security-relevant half of external sign-in and its cases are
/// exhaustible. Asked only about a <b>first</b> link and only about an address the provider itself
/// vouched for — once the identity is on file, the address stops being consulted (see
/// <see cref="IExternalLoginsService.FindByExternalLoginAsync"/>).</summary>
public static class ExternalAccountLinkPolicy
{
    /// <param name="match">
    /// The local account holding the vouched-for address, or <c>null</c> when none does. Never passed
    /// for an address the provider did not itself confirm.
    /// </param>
    public static ExternalAccountLinkDecision Decide(LocalAccountMatch? match) => match switch
    {
        // Nobody holds the address. The provider checked it, so the account created from it starts
        // confirmed and passwordless.
        null => ExternalAccountLinkDecision.Provision,

        // Someone holds it and proved they can read mail there, and the provider says the same of the
        // person signing in now: two independent proofs of one address.
        { EmailConfirmed: true } => ExternalAccountLinkDecision.Link,

        // Someone holds it and never proved anything: an attacker registers victim@example.com,
        // never confirms it, and an automatic link would hand them the victim's account.
        _ => ExternalAccountLinkDecision.Refuse,
    };
}
