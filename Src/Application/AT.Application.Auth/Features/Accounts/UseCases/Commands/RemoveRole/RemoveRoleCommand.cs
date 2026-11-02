namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RemoveRole;

public sealed record RemoveRoleCommand(Guid UserId, string Role);
