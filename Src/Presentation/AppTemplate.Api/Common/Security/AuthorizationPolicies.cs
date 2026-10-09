namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Named authorisation policies beyond "any authenticated user", which
/// <c>Program.cs</c>'s fallback policy already requires of everything. The names live here, where any
/// controller may use them; what each policy requires is registered by the feature that knows it —
/// <see cref="Administrator"/> by the authentication feature, which owns the role.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Administrator = "Administrator";
}
