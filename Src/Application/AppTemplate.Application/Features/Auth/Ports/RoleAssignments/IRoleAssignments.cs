namespace AppTemplate.Application.Features.Auth.Ports.RoleAssignments;

/// <summary>
/// Granting and revoking a role. The role name is opaque here: this module seeds and knows about
/// exactly one (see <c>IdentityRoles</c>), but nothing about assigning one is specific to which role
/// it is, so a project that seeds more does not need a second port.
/// </summary>
public interface IRoleAssignments
{
    Task<RoleAssignmentChangeOutcome> AddRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<RoleAssignmentChangeOutcome> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);
}
