using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Application.Auth.UnitTests.TestDoubles;

/// <summary>
/// The session the caller's access token names, and the client it makes the request from. Both
/// answers are fixed at construction, like <see cref="StubCurrentUser"/>'s one.
/// </summary>
internal sealed class StubCurrentSession(Guid? sessionId, SessionClient client) : ICurrentSession
{
    /// <summary>
    /// A token that names no session — issued before sessions were named in it, or by no auth
    /// module of ours. A use case that would spare the caller's session spares nothing on this.
    /// </summary>
    public static StubCurrentSession WithoutSession { get; } = new(null, SessionClient.Unknown);

    /// <summary>A caller whose token names a session, from a client that said nothing about itself.</summary>
    public static StubCurrentSession InSession(Guid sessionId) => new(sessionId, SessionClient.Unknown);

    public Guid? SessionId => sessionId;

    public SessionClient Client => client;
}
