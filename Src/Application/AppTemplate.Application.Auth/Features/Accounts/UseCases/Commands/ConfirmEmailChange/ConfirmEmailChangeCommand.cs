namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmailChange;

public sealed record ConfirmEmailChangeCommand(string NewEmail, string Token);
