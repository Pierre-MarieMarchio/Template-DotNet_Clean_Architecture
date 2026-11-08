using System.Security.Claims;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Model.Policies;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.Features.ExternalIdentity.Composition;

internal sealed class ExternalPrincipalMapper(
    IExternalIdentityService identities,
    IOptions<AuthenticationExternalOptions> options)
{
    private const string _emailVerifiedClaim = "email_verified";

    internal async Task<ClaimsPrincipal?> MapAsync(
        ClaimsPrincipal tokenPrincipal,
        SecurityToken? token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenPrincipal);

        var settings = options.Value;

        string? issuer = token?.Issuer;
        string? subject = tokenPrincipal.FindFirst(settings.SubjectClaim)?.Value;

        if (string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(subject)
            || issuer.Length > ExternalIdentityPolicy.IssuerMaxLength
            || subject.Length > ExternalIdentityPolicy.SubjectMaxLength)
        {
            return null;
        }

        var userId = await identities.ResolveAsync(
            new ExternalCallerDto(
                issuer,
                subject,
                VerifiedEmail(tokenPrincipal, settings.EmailClaim),
                tokenPrincipal.FindFirst(settings.NameClaim)?.Value),
            cancellationToken);

        var roles = settings.Roles.Source == AuthenticationExternalOptions.RoleSource.Local
            ? await identities.ListAssignedRolesAsync(userId, cancellationToken)
            : TokenRoleMapping.Read((token as JsonWebToken)?.EncodedPayload, settings.Roles.ClaimPath!);

        var identity = new ClaimsIdentity(
            tokenPrincipal.Claims
                .Where(claim => claim.Type is not (ClaimTypes.NameIdentifier or ClaimTypes.Role))
                .Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer)),
            tokenPrincipal.Identity?.AuthenticationType ?? JwtBearerDefaults.AuthenticationScheme,
            settings.NameClaim,
            ClaimTypes.Role);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));

        foreach (string role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private static string? VerifiedEmail(ClaimsPrincipal tokenPrincipal, string emailClaim)
    {
        string? verified = tokenPrincipal.FindFirst(_emailVerifiedClaim)?.Value;

        return verified is null || string.Equals(verified, bool.TrueString, StringComparison.OrdinalIgnoreCase)
            ? tokenPrincipal.FindFirst(emailClaim)?.Value
            : null;
    }
}
