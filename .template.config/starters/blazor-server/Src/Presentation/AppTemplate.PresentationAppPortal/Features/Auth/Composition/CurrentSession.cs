using System.Security.Claims;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.PresentationAppPortal.Common.Security;

namespace AppTemplate.PresentationAppPortal.Features.Auth.Composition;

internal sealed class CurrentSession(CallerPrincipal caller, IHttpContextAccessor httpContextAccessor) : ICurrentSession
{
    public Guid? SessionId =>
        Guid.TryParse(caller.Principal.FindFirstValue("sid"), out var session) ? session : null;

    public SessionClient Client =>
        httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent
            ? new SessionClient(userAgent)
            : SessionClient.Unknown;
}
