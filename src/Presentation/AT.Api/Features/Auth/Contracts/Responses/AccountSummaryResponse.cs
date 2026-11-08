namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>One account as an administrator sees it in the list.</summary>
public sealed record AccountSummaryResponse(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    bool LockedOut,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);
