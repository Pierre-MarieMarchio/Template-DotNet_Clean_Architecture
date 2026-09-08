namespace AppTemplate.Application.Auth.Features.Auth.Ports.RoleAssignments;

/// <summary>How granting or revoking a role ended.</summary>
public enum RoleAssignmentChangeStatus
{
    /// <summary>
    /// The assignment matches what was asked. It reaches the holder's own requests at their next
    /// access token rather than this instant, since claims are read at issuance.
    /// </summary>
    Applied,

    /// <summary>No account has that id.</summary>
    NoSuchAccount,

    /// <summary>The store refused the change itself — an unknown role, or one already in that state.</summary>
    Rejected,
}
