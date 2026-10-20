namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.AddRole;

public sealed record AddRoleCommand(Guid UserId, string Role);
