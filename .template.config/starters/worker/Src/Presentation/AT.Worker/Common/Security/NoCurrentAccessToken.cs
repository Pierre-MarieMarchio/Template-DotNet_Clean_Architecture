using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Worker.Common.Security;

internal sealed class NoCurrentAccessToken : ICurrentAccessToken
{
    public DateTimeOffset? IssuedAt => null;
}
