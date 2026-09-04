namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Register;

public sealed record RegisterCommand(string UserName, string Email, string Password);
