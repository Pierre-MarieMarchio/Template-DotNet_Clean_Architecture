namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;

public sealed record AccountSummary(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    bool LockedOut,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);
