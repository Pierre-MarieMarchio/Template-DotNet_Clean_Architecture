namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

public sealed record UserProfile(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled,
    bool HasPassword);
