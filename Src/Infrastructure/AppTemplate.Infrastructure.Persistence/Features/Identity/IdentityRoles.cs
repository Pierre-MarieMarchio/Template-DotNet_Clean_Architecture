namespace AppTemplate.Infrastructure.Persistence.Features.Identity;

/// <summary>
/// The role names this application seeds and authorises against.
/// <para>
/// Public, and deliberately the one place a role name is spelled. The seeder creates the row and the
/// API's authorisation policy requires it, and those two live in different assemblies: if each held
/// its own literal, renaming the role in one would leave the other requiring a role nobody has —
/// which fails closed, as a 403 on every administrative request, but only once somebody tries.
/// </para>
/// </summary>
/// <remarks>
/// It sits in the persistence module because that is where the role is created. Nothing else here is
/// public, but this is a constant rather than an adapter: naming it grants no access to a row, a
/// context or a store.
/// </remarks>
public static class IdentityRoles
{
    /// <summary>Full administrative rights. Seeded by <c>IdentitySeeder</c>.</summary>
    public const string Administrator = "Admin";
}
