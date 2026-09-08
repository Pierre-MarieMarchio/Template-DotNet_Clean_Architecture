namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetCurrentUser;

/// <summary>
/// The caller's own account as the store holds it now, not as their access token remembers it.
/// </summary>
/// <param name="Roles">
/// Read fresh, so a grant or revocation shows here before the caller's next token carries it.
/// </param>
public sealed record GetCurrentUserOutcome(
    Guid UserId,
    string UserName,
    string Email,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool TwoFactorEnabled);
