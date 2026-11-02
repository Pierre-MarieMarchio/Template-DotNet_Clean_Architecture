using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Application.Auth.UnitTests.TestDoubles;

internal sealed class StubCurrentSession(Guid? sessionId, SessionClient client) : ICurrentSession
{
    public static StubCurrentSession WithoutSession { get; } = new(null, SessionClient.Unknown);

    public static StubCurrentSession InSession(Guid sessionId) => new(sessionId, SessionClient.Unknown);

    public Guid? SessionId => sessionId;

    public SessionClient Client => client;
}
