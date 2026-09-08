namespace AppTemplate.Application.Auth.Features.Auth.Ports.AccountLockouts;

/// <summary>How an administrative lock or unlock ended.</summary>
public enum LockoutChangeStatus
{
    /// <summary>The account now carries the lockout state the caller asked for.</summary>
    Applied,

    /// <summary>No account has that id, so there was nothing to suspend or release.</summary>
    NoSuchAccount,

    /// <summary>The account was found but the store refused the change itself.</summary>
    Rejected,
}
