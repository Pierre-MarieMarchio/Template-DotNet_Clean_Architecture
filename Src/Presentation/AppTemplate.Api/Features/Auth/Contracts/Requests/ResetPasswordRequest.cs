namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>
/// The reset token mailed to the address and the new password, sent in the body as <see
/// cref="ConfirmEmailRequest"/> is.
/// </summary>
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
