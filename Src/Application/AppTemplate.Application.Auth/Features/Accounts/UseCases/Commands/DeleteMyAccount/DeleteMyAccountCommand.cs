namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;

public sealed record DeleteMyAccountCommand(string? CurrentPassword, string? TwoFactorCode);
