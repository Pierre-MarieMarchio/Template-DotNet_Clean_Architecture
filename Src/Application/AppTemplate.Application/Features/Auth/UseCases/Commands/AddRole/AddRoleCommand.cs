namespace AppTemplate.Application.Features.Auth.UseCases.Commands.AddRole;

public sealed record AddRoleCommand(Guid UserId, string Role);
