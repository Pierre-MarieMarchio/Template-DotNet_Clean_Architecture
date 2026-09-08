namespace AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

/// <param name="Account">
/// Present when the credential was verified, and optionally on a refusal against a known account —
/// so the caller can attribute the failure to a user id in an audit trail without ever being handed
/// the address that was typed.
/// </param>
public sealed record CredentialCheckOutcome(CredentialCheckStatus Status, AccountIdentity? Account)
{
    /// <summary>The password matched and sign-in is permitted.</summary>
    public static CredentialCheckOutcome Verified(AccountIdentity account) =>
        new(CredentialCheckStatus.Verified, account);

    /// <summary>
    /// Sign-in was refused for one of the reasons on <see cref="CredentialCheckStatus"/>. The account
    /// is optional because a refusal against an unknown address has none to name.
    /// </summary>
    public static CredentialCheckOutcome Refused(CredentialCheckStatus outcome, AccountIdentity? account = null) =>
        new(outcome, account);
}
