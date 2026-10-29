using System.Security.Claims;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Model.Policies;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.PresentationApp.Features.ExternalIdentity.Composition;

internal sealed class ExternalSignInMapping(
    IExternalIdentityService identities,
    IOptions<AuthenticationExternalOptions> options)
{
    private const string _emailVerifiedClaim = "email_verified";

    public async Task<ClaimsPrincipal?> MapAsync(
        ClaimsPrincipal idTokenPrincipal,
        SecurityToken? idToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idTokenPrincipal);

        var settings = options.Value;

        string? issuer = idToken?.Issuer;
        string? subject = idTokenPrincipal.FindFirst(settings.SubjectClaim)?.Value;

        if (string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(subject)
            || issuer.Length > ExternalIdentityPolicy.IssuerMaxLength
            || subject.Length > ExternalIdentityPolicy.SubjectMaxLength)
        {
            return null;
        }

        string? verified = idTokenPrincipal.FindFirst(_emailVerifiedClaim)?.Value;
        string? email = verified is null || string.Equals(verified, bool.TrueString, StringComparison.OrdinalIgnoreCase)
            ? idTokenPrincipal.FindFirst(settings.EmailClaim)?.Value
            : null;
        string? name = idTokenPrincipal.FindFirst(settings.NameClaim)?.Value;

        var userId = await identities.ResolveAsync(new ExternalCallerDto(issuer, subject, email, name), cancellationToken);

        var roles = settings.Roles.Source == AuthenticationExternalOptions.RoleSource.Local
            ? await identities.ListAssignedRolesAsync(userId, cancellationToken)
            : TokenRoleMapping.Read((idToken as JsonWebToken)?.EncodedPayload, settings.Roles.ClaimPath!);

        var identity = new ClaimsIdentity(
            CookieAuthenticationDefaults.AuthenticationScheme,
            settings.NameClaim,
            ClaimTypes.Role);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));

        if (name is not null)
        {
            identity.AddClaim(new Claim(settings.NameClaim, name));
        }

        foreach (string role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }
}
