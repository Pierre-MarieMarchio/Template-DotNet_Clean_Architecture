namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Email, string Token);
