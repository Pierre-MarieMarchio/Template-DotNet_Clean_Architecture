namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemoveRole;

/// <param name="UserId">The account to revoke from. Refused when it is the caller's own.</param>
/// <param name="Role">A role name, non-empty and otherwise the store's to recognise or refuse.</param>
public sealed record RemoveRoleCommand(Guid UserId, string Role);
