using AppTemplate.Infrastructure.Auth.Features.Auth.Seeding;
using Microsoft.AspNetCore.Authorization;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Named authorisation policies beyond "any authenticated user", which
/// <c>Program.cs</c>'s fallback policy already requires of everything.
/// </summary>
public static class AuthorizationPolicies
{
    public const string Administrator = "Administrator";

    /// <summary>
    /// Adds <see cref="Administrator"/> alongside whatever policies are already configured. A second
    /// builder rather than a parameter threaded into the one already in <c>Program.cs</c>: both
    /// configure the same <see cref="AuthorizationOptions"/> instance, so this leaves the
    /// default-deny fallback policy set there completely untouched.
    /// </summary>
    public static IServiceCollection AddApiAuthorizationPolicies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .AddPolicy(Administrator, policy => policy.RequireRole(IdentityRoles.Administrator));

        return services;
    }
}
