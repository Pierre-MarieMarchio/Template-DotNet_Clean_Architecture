namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;

/// <summary>
/// Sent in a request body rather than a query string, for the reason <c>ConfirmEmailCommand</c>
/// gives.
/// </summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);
