namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public sealed record CreateAccessKeyOutcome(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Key);
