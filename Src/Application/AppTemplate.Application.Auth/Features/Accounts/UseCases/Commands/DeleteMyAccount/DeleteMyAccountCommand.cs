namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;

/// <summary>Carries no identity: the account is the authenticated caller's own.</summary>
/// <param name="CurrentPassword">
/// The account's password, when it has one — which evidence is required is decided from the
/// profile, not from what this carries.
/// </param>
/// <param name="TwoFactorCode">
/// A code from the armed second factor, when one is armed — the authenticator app's current code or
/// a recovery code, as at sign-in.
/// </param>
public sealed record DeleteMyAccountCommand(string? CurrentPassword, string? TwoFactorCode);
