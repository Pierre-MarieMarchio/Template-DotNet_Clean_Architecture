namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;

public sealed record GetCurrentUserOutcome(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled);
