using System.Globalization;
using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Api.Common.Security;

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
