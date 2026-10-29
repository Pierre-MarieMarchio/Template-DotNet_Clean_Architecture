namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public sealed record CreateAccessKeyCommand(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    string? CurrentPassword);
