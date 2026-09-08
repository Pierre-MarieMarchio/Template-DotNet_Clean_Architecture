namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;

/// <summary>Carries no identity: the account is the authenticated caller's own.</summary>
/// <param name="CurrentPassword">
/// Proof the request comes from the holder and not merely from a live session, which is what stops
/// a stolen token from taking the account over outright.
/// </param>
/// <param name="NewPassword">
/// Length-checked against <c>PasswordPolicy</c> before the store sees it; the character-class rules
/// are the store's to apply.
/// </param>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);
