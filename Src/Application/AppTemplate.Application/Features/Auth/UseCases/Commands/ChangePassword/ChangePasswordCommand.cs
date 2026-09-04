namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);
