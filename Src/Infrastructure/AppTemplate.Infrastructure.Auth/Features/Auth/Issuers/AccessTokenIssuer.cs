using System.Security.Claims;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Issuers;

/// <summary>
/// Signs access tokens for <c>SessionTokensService</c>. Uses <see cref="JsonWebTokenHandler"/> rather than the legacy
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
    IDateTimeProvider dateTimeProvider)
{
    private readonly JsonWebTokenHandler _handler = new();

    /// <exception cref="InvalidOperationException">No account has that id.</exception>
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

        // The session the token is issued in, so a later request can tell which one it is made
        // from. The short name is on purpose: no inbound claim-type mapping renames it, so the
        // claim reads back exactly as it was written.
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
