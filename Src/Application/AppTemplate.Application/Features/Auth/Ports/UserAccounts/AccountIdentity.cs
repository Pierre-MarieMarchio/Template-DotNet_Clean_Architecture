namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <summary>The identifying facts about an account, as everything outside the user store sees it.</summary>
/// <param name="TwoFactorEnabled">
/// Read by <c>LoginUseCase</c> to decide whether a verified password is enough to issue tokens or a
/// second-factor challenge has to come first.
/// </param>
public sealed record AccountIdentity(Guid UserId, string UserName, string Email, bool TwoFactorEnabled);
