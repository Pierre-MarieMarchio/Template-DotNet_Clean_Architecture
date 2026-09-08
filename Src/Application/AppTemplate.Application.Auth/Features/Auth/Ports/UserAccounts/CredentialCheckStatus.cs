namespace AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;

/// <summary>
/// Why a credential check ended the way it did.
/// <para>
/// The reasons are separated here so that the sign-in policy is visible and testable where the
/// decision is made. Every one of them that is not <see cref="Verified"/> must reach the caller as
/// the same error: telling them apart is precisely what a user-enumeration probe is after.
/// </para>
/// </summary>
public enum CredentialCheckStatus
{
    /// <summary>The password matched and nothing else stands in the way of signing in.</summary>
    Verified,

    /// <summary>No account holds that address.</summary>
    NoSuchAccount,

    /// <summary>The address names an account and the password did not match it.</summary>
    IncorrectPassword,

    /// <summary>
    /// The password matched but sign-in is not permitted, which under this configuration means the
    /// address has not been confirmed.
    /// </summary>
    EmailNotConfirmed,

    /// <summary>Too many failed attempts. Saying so would confirm the account exists.</summary>
    LockedOut,
}
