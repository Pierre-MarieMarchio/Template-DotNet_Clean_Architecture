using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IRoleAssignments"/> over <see cref="UserManager{TUser}"/>. The role name is passed
/// through unexamined — this module knows no role names of its own.
/// <para>
/// <see cref="RoleManager{TRole}"/> is here only to guard <see cref="AddRoleAsync"/> against a role
/// that was never seeded. <c>UserManager.AddToRoleAsync</c> checks whether the user already carries
/// the role before it touches the store, but not whether the role exists at all — the Entity
/// Framework store underneath throws <see cref="InvalidOperationException"/> for that case instead
/// of returning a refusal, which would otherwise turn an unknown role name into an unhandled
/// exception rather than a reported <see cref="RoleAssignmentChangeStatus.Rejected"/>.
/// <c>RemoveFromRoleAsync</c> needs no equivalent guard: removing a role nobody has is already a
/// normal, non-throwing refusal, because "is this user in that role" is false for a role that does
/// not exist just as much as for one that does but was never granted.
/// </para>
/// </summary>
internal sealed class RoleAssignments(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IAppUserDirectory directory) : IRoleAssignments
{
    public async Task<RoleAssignmentChangeOutcome> AddRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return RoleAssignmentChangeOutcome.NoSuchAccount;
        }

        if (!await roleManager.RoleExistsAsync(role))
        {
            return RoleAssignmentChangeOutcome.Rejected($"Role '{role}' does not exist.");
        }

        var result = await userManager.AddToRoleAsync(user, role);

        if (!result.Succeeded)
        {
            return RoleAssignmentChangeOutcome.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        // AddToRoleAsync does not rotate the security stamp on its own. Without this, a role granted
        // just now has no effect on a caller's access until their current access token expires and
        // they sign in again — the new permission simply does not exist yet as far as any request
        // already in flight with an older token is concerned.
        await userManager.UpdateSecurityStampAsync(user);

        return RoleAssignmentChangeOutcome.Applied;
    }

    public async Task<RoleAssignmentChangeOutcome> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return RoleAssignmentChangeOutcome.NoSuchAccount;
        }

        var result = await userManager.RemoveFromRoleAsync(user, role);

        if (!result.Succeeded)
        {
            return RoleAssignmentChangeOutcome.Rejected(string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        // Same gap as AddRoleAsync, in the other direction: without this, a role just revoked keeps
        // authorising whatever it granted for as long as the caller's current access token remains
        // valid.
        await userManager.UpdateSecurityStampAsync(user);

        return RoleAssignmentChangeOutcome.Applied;
    }
}
