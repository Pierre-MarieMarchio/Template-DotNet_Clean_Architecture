namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;

public sealed record RequestEmailChangeCommand(string CurrentPassword, string NewEmail);
