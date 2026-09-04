namespace AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;

public sealed record CurrentUserResponse(
    Guid UserId,
    string UserName,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled);
