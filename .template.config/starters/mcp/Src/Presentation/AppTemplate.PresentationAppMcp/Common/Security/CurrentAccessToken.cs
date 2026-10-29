using System.Globalization;
using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.PresentationApp.Common.Security;

internal sealed class CurrentAccessToken(IHttpContextAccessor httpContextAccessor) : ICurrentAccessToken
{
    public DateTimeOffset? IssuedAt =>
        long.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue("iat"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
