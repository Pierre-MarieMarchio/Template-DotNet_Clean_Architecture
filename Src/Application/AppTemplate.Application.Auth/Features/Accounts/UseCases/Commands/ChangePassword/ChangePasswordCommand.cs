namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);
