using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.PresentationAppMcp.Features.Auth.Composition;

internal sealed class CurrentSession(IHttpContextAccessor httpContextAccessor) : ICurrentSession
{
    public Guid? SessionId => null;

    public SessionClient Client =>
        httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent
            ? new SessionClient(userAgent)
            : SessionClient.Unknown;
}
