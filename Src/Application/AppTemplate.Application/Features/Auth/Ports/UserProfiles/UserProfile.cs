namespace AppTemplate.Application.Features.Auth.Ports.UserProfiles;

/// <summary>The account's own profile, as the user store holds it now.</summary>
public sealed record UserProfile(
    Guid UserId,
    string UserName,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled);
