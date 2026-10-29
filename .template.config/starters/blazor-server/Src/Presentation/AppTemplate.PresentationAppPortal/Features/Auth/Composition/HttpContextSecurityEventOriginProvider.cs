using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;

namespace AppTemplate.PresentationAppPortal.Features.Auth.Composition;

internal sealed class HttpContextSecurityEventOriginProvider(IHttpContextAccessor httpContextAccessor)
    : ISecurityEventOriginProvider
{
    public string? IpAddress =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent =>
        httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent
            ? userAgent
            : null;
}
