namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResetPassword;

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);
