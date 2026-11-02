using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

internal static class ExternalProvider
{
    internal const string Issuer = "https://idp.integration.test/realms/app";

    internal const string Audience = "app-template-api";

    internal const string RolesClaimPath = "realm_access.roles";

    internal const string AdministratorRole = "app-admin";

    private static readonly RSA _key = RSA.Create(2048);

    internal static SecurityKey SigningKey { get; } = new RsaSecurityKey(_key) { KeyId = "integration" };

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
