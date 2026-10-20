namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <summary>How registering an address ended.</summary>
public enum AccountRegistrationStatus
{
    /// <summary>A new account holds the address.</summary>
    Created,

    /// <summary>An existing account holds the address.</summary>
    AlreadyRegistered,

    /// <summary>The store refused the submitted values themselves.</summary>
    Rejected,
}
