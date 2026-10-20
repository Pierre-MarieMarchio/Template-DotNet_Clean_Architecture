namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <summary>How creating an account ended.</summary>
public enum AccountCreationStatus
{
    /// <summary>The account exists.</summary>
    Created,

    /// <summary>The address is taken.</summary>
    Conflict,

    /// <summary>
    /// The store refused the values themselves — password policy, format.
    /// </summary>
    Rejected,
}
