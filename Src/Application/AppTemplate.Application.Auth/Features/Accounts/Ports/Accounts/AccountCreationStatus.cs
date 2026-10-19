namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <summary>How creating an account ended.</summary>
public enum AccountCreationStatus
{
    /// <summary>The account exists.</summary>
    Created,

    /// <summary>The user name or the email address is taken. Which one is deliberately not said.</summary>
    Conflict,

    /// <summary>
    /// The store refused the values themselves — password policy, allowed characters, format.
    /// </summary>
    Rejected,
}
