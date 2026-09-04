namespace AppTemplate.Application.Features.Auth.Ports.RoleAssignments;

public enum RoleAssignmentChangeStatus
{
    Applied,

    NoSuchAccount,

    /// <summary>The store refused the change itself — an unknown role, or one already in that state.</summary>
    Rejected,
}
