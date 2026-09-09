using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;

namespace AppTemplate.Application.Auth.Features.Auth.Policies;

/// <summary>
/// Whether a provider identity signing in for the first time may take over a local account, given
/// what that address already points at.
/// <para>
/// A pure function of one input, kept out of the use case on purpose: it is the security-relevant
/// half of external sign-in, its cases are exhaustible, and a test can enumerate them without
/// standing up a token verifier, four substitutes and a token issuer.
/// </para>
/// <para>
/// It is only ever asked about a <b>first</b> link, and only about an address the provider itself
/// vouched for. Once the provider identity is on file the address stops being consulted at all — see
/// <see cref="IExternalLoginsService.FindByExternalLoginAsync"/> for why resolving by address breaks
/// on the second Apple sign-in.
/// </para>
/// </summary>
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
