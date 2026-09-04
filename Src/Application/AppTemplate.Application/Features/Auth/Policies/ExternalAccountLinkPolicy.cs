using AppTemplate.Application.Features.Auth.Ports.ExternalLogins;

namespace AppTemplate.Application.Features.Auth.Policies;

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
    public static ExternalAccountLinkDecision Decide(LocalAccountMatch? match) => match switch
    {
        // Nobody holds the address. The provider checked it, so the account created from it starts
        // confirmed and passwordless.
        null => ExternalAccountLinkDecision.Provision,

        // Someone holds it and proved they can read mail there, and the provider says the same thing
        // about the person signing in now. Two independent proofs of the same address are the best
        // evidence either side has that this is one person, so the accounts are joined.
        { EmailConfirmed: true } => ExternalAccountLinkDecision.Link,

        // Someone holds it and never proved anything. This is the whole reason the rule exists: an
        // attacker registers victim@example.com, never confirms it, waits, and an automatic link
        // hands them the account the victim believes they are creating through their provider.
        _ => ExternalAccountLinkDecision.Refuse,
    };
}
