namespace AppTemplate.Application.Auth.Features.Auth.Ports.RoleAssignments;

/// <summary>
/// Granting and revoking a role. The role name is opaque here: this module seeds and knows about
/// exactly one (see <c>IdentityRoles</c>), but nothing about assigning one is specific to which role
/// it is, so a project that seeds more does not need a second port.
/// </summary>
public interface IRoleAssignmentsService
{
    /// <summary>
    /// Grants the role. An unknown role, or one the account already holds, comes back as
    /// <see cref="RoleAssignmentChangeStatus.Rejected"/> rather than as a silent success.
    /// </summary>
    Task<RoleAssignmentChangeOutcome> AddRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the role, on the same terms as <see cref="AddRoleAsync"/>: a role the account does not
    /// hold is the store's refusal, not a no-op.
    /// </summary>
    Task<RoleAssignmentChangeOutcome> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);
}
