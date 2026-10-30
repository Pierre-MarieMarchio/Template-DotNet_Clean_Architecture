using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace AppTemplate.PresentationAppMcp.UnitTests.TestSupport;

internal static class TestTokens
{
    internal const string Issuer = "https://idp.example.com/realms/app";

    internal const string ApiAudience = "app-api";

    private static readonly RsaSecurityKey _providerKey = new(RSA.Create(2048)) { KeyId = "provider" };

    private static readonly ConcurrentDictionary<string, Guid> _owners = new(StringComparer.Ordinal);

    internal static Guid OwnerOf(string subject) => _owners.GetOrAdd(subject, _ => Guid.CreateVersion7());

    internal static string For(string subject, string audience = McpHost.Resource) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_providerKey, SecurityAlgorithms.RsaSha256),
        });

    internal static void Trust(IServiceCollection services)
    {
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.Configuration.SigningKeys.Add(_providerKey);
        });

        var identities = Substitute.For<IExternalIdentityService>();
        identities.ResolveAsync(Arg.Any<ExternalCallerDto>(), Arg.Any<CancellationToken>())
            .Returns(call => UserId.Create(OwnerOf(call.Arg<ExternalCallerDto>().Subject)));
        identities.ListAssignedRolesAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns([]);

        services.RemoveAll<IExternalIdentityService>();
        services.AddSingleton(identities);
    }
}
