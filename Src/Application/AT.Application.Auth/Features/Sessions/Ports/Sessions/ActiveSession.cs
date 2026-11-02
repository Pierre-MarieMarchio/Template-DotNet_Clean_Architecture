namespace AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;

public sealed record ActiveSession(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, string? UserAgent);
