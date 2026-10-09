using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for the external OpenID Connect provider: an issuer, an audience and a key generated
/// for the run. <see cref="ApiFactory"/> hands the host what the provider's discovery document would
/// have said, so the bearer handler validates these tokens exactly as it would a real provider's,
/// signature, issuer, audience and lifetime included.
/// </summary>
internal static class ExternalProvider
{
    internal const string Issuer = "https://idp.integration.test/realms/app";

    internal const string Audience = "app-template-api";

    /// <summary>The dot path the test host reads roles from, Keycloak's shape.</summary>
    internal const string RolesClaimPath = "realm_access.roles";

    /// <summary>The role the test host's <c>Administrator</c> policy requires.</summary>
    internal const string AdministratorRole = "app-admin";

    private static readonly RSA _key = RSA.Create(2048);

    internal static SecurityKey SigningKey { get; } = new RsaSecurityKey(_key) { KeyId = "integration" };

    /// <summary>
    /// A token for <paramref name="subject"/>, valid for ten minutes of machine time: the bearer
    /// handler checks the lifetime against the machine clock, not against the host's injected one.
    /// </summary>
    internal static string Token(string subject, string email, params string[] roles)
    {
        DateTime now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(10),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = subject,
                ["email"] = email,
                ["realm_access"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["roles"] = roles },
            },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
