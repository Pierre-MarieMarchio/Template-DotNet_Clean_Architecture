using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Worker.Features.Auth.Composition;

internal sealed class NoCallerCurrentSession : ICurrentSession
{
    public Guid? SessionId => throw new NotSupportedException(
        "This host has no current session: it runs with no request and no principal. A use case " +
        $"that reads {nameof(ICurrentSession)}.{nameof(SessionId)} cannot run unmodified from here.");

    public SessionClient Client => SessionClient.Unknown;
}
