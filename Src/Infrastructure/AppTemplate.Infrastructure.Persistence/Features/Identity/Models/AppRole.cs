using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Models;

/// <summary>
/// A role. Like <see cref="AppUser"/>, a framework persistence type with no domain twin.
/// <para>
/// A parameterless constructor is mandatory: <see cref="RoleManager{TRole}"/>, the EF materialiser
/// and every serialiser need one, so this type cannot be reduced to a primary constructor taking
/// the role name.
/// </para>
/// </summary>
public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
    }

    public AppRole(string roleName) : base(roleName)
    {
    }
}
