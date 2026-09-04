namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RemoveRole;

public sealed record RemoveRoleCommand(Guid UserId, string Role);
