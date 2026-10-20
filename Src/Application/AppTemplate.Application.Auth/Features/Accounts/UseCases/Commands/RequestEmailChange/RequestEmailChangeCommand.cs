namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestEmailChange;

public sealed record RequestEmailChangeCommand(string CurrentPassword, string NewEmail);
