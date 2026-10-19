namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

/// <param name="SessionId">One of the caller's sessions, as the session list names it. May be the
/// session the request is made from, which signs it out.</param>
public sealed record RevokeSessionCommand(Guid SessionId);
