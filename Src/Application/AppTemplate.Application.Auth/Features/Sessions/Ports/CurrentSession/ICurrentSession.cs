using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;

/// <summary>
/// The session a request is made from, and the client making it. Implemented by the host, which
/// alone sees the request, as <c>ICurrentUser</c> is.
/// </summary>
public interface ICurrentSession
{
    /// <summary>
    /// The <c>sid</c> claim of the caller's access token, or <c>null</c> when the request is
    /// anonymous or its token predates the claim. <c>null</c> means "unknown", never "none": a use
    /// case that would spare the caller's session spares nothing on <c>null</c>.
    /// </summary>
    Guid? SessionId { get; }

    /// <summary>The client sending this request, recorded on a session it starts or extends.</summary>
    SessionClient Client { get; }
}
