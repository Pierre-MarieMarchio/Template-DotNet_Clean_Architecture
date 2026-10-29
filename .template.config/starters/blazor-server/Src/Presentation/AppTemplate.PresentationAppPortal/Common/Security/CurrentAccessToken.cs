using System.Globalization;
using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.PresentationAppPortal.Common.Security;

internal sealed class CurrentAccessToken(CallerPrincipal caller) : ICurrentAccessToken
{
    public DateTimeOffset? IssuedAt =>
        long.TryParse(caller.Principal.FindFirst("iat")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
