namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;

/// <summary>How an administrative change to an account ended.</summary>
public enum AccountAdministrationStatus
{
    /// <summary>The account is now in the state the administrator asked for.</summary>
    Applied,

    /// <summary>No account has that id, so nothing changed.</summary>
    NoSuchAccount,

    /// <summary>The account was found but the store refused the change itself.</summary>
    Rejected,
}
