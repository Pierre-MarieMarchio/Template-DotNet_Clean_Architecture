using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;

namespace AppTemplate.Api.Features.Auth.Composition;

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
