namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>The authenticated caller's own profile.</summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled);
