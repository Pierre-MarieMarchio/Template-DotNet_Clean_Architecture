namespace AppTemplate.Application.Features.Auth.Ports;

public enum AccountCreationOutcome
{
    Created,

    /// <summary>The user name or the email address is taken. Which one is deliberately not said.</summary>
    Conflict,

    /// <summary>
    /// The store refused the values themselves — password policy, allowed characters, format.
    /// </summary>
    Rejected,
}
