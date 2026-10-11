using System.Globalization;
using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// <see cref="ICurrentAccessToken"/> over the request's validated principal, the same place
/// <see cref="CurrentUser"/> reads the caller's id from. The issued-at claim is the token
/// handler's own <c>iat</c>, which carries the instant as Unix time seconds; a claim that is absent
/// or unreadable answers <c>null</c>, and the decision that reads it refuses rather than guesses.
/// No scheme is named: whichever way this host authenticates its callers, the fact asked of it is
/// the same one.
/// </summary>
internal sealed class CurrentAccessToken(IHttpContextAccessor httpContextAccessor) : ICurrentAccessToken
{
    public DateTimeOffset? IssuedAt
    {
        get
        {
            string? issuedAt = httpContextAccessor.HttpContext?.User.FindFirstValue("iat");

            return long.TryParse(issuedAt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
    }
}
