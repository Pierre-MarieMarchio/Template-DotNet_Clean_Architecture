namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

public sealed record SessionClient(string? UserAgent)
{
    public static SessionClient Unknown { get; } = new((string?)null);
}
