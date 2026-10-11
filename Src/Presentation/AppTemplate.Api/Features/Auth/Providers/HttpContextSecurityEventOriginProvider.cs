using AppTemplate.Infrastructure.Auth.Features.Auth.Providers;

namespace AppTemplate.Api.Features.Auth.Providers;

/// <summary>
/// The origin behind a security event, as this host can tell it: the remote address after the
/// forwarded headers were applied, and the request's <c>User-Agent</c>. A scope with no request
/// behind it — the maintenance timer's — has no origin to name, and this answers <c>null</c>
/// rather than throwing: an absent origin is the truthful answer, not a refusal, and the event
/// is worth recording either way.
/// </summary>
internal sealed class HttpContextSecurityEventOriginProvider(IHttpContextAccessor httpContextAccessor)
    : ISecurityEventOriginProvider
{
    public string? IpAddress =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            string? userAgent = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

            return string.IsNullOrEmpty(userAgent) ? null : userAgent;
        }
    }
}
