namespace AppTemplate.Application.Features.Auth.Ports.AccountLockouts;

public enum LockoutChangeOutcome
{
    Applied,

    NoSuchAccount,

    /// <summary>The account was found but the store refused the change itself.</summary>
    Rejected,
}
