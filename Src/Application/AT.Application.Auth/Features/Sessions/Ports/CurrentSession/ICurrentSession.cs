using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;

public interface ICurrentSession
{
    Guid? SessionId { get; }

    SessionClient Client { get; }
}
