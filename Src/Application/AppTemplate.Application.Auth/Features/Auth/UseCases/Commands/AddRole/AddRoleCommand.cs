namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.AddRole;

/// <param name="UserId">The account to grant to: an administrator names their target.</param>
/// <param name="Role">
/// A role name, non-empty and otherwise unchecked here: which names exist is the store's answer,
/// and this project seeds exactly one.
/// </param>
public sealed record AddRoleCommand(Guid UserId, string Role);
