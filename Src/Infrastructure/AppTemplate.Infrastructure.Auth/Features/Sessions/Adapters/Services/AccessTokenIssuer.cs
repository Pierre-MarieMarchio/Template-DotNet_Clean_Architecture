using System.Security.Claims;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;

internal sealed class AccessTokenIssuer(
    IAppUserDirectory directory,
    IOptions<JwtOptions> options,
    IDateTimeProvider dateTimeProvider)
{
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<IssuedAccessToken> IssueAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account with id '{userId}' exists.");

        var claims = await directory.GenerateClaimsAsync(user, cancellationToken);

        var settings = options.Value;
        var issuedAt = dateTimeProvider.UtcNow;
        var expiresAt = issuedAt.AddMinutes(settings.AccessTokenLifetimeInMinutes);

        var identity = new ClaimsIdentity(claims);
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));

        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sid, sessionId.ToString()));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = identity,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
