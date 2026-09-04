using System.Security.Claims;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Issuers;

/// <summary>
/// Signs access tokens. Uses <see cref="JsonWebTokenHandler"/> rather than the legacy
/// <c>JwtSecurityTokenHandler</c>, and every timestamp comes from <see cref="IDateTimeProvider"/> in
/// UTC so that <c>exp</c> agrees with what the rest of the system records.
/// <para>
/// The claim set is read from the account on every issuance rather than carried in from the caller,
/// which is what makes a revoked role or a rotated security stamp take effect at the next refresh.
/// </para>
/// </summary>
internal sealed class AccessTokenIssuer(
    IAppUserDirectory directory,
    IOptions<JwtOptions> options,
    IDateTimeProvider dateTimeProvider) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<IssuedAccessToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account with id '{userId}' exists.");

        var claims = await directory.GenerateClaimsAsync(user, cancellationToken);

        var settings = options.Value;
        var issuedAt = dateTimeProvider.UtcNow;
        var expiresAt = issuedAt.AddMinutes(settings.AccessTokenLifetimeInMinutes);

        var identity = new ClaimsIdentity(claims);
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));

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
