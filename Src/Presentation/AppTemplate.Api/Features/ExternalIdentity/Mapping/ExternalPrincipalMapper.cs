using System.Security.Claims;
using AppTemplate.Api.Features.ExternalIdentity.Options;
using AppTemplate.Application.Features.ExternalIdentity.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Policies;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.Features.ExternalIdentity.Mapping;

/// <summary>
/// Turns the principal a validated provider token produced into the one this API serves: the
/// internal owner id in <see cref="ClaimTypes.NameIdentifier"/>, which <c>CurrentUser</c> reads, and
/// the caller's roles as <see cref="ClaimTypes.Role"/> claims, which the authorisation policies read.
/// <para>
/// <b>Fails closed.</b> A token without an issuer or a subject, or with one too long to be a key, is
/// answered with <c>null</c> and the request is refused as unauthenticated. There is no anonymous or
/// shared fallback owner, and the provider's subject never becomes the owner id itself, even when it
/// happens to be a GUID. A store failure propagates and fails the request.
/// </para>
/// </summary>
internal sealed class ExternalPrincipalMapper(
    IExternalIdentityService identities,
    IOptions<AuthenticationExternalOptions> options)
{
    /// <returns>The caller's principal, or <c>null</c> when the token cannot name one.</returns>
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
                tokenPrincipal.FindFirst(settings.EmailClaim)?.Value,
                tokenPrincipal.FindFirst(settings.NameClaim)?.Value),
            cancellationToken);

        var roles = settings.Roles.Source == AuthenticationExternalOptions.RoleSource.Local
            ? await identities.ListAssignedRolesAsync(userId, cancellationToken)
            : TokenRoleMapping.Read(token, settings.Roles.ClaimPath!);

        // The token's own claims are kept for whoever reads them, except the two types this API
        // trusts: a provider-sent NameIdentifier or role claim must not stand beside the mapped ones.
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
}
