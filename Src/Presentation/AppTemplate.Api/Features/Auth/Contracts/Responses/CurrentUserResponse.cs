namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>The authenticated caller's own profile: the one place the full account is published.</summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string UserName,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled);
