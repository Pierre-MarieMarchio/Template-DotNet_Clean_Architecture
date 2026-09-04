namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <summary>The identifying facts about an account, as everything outside the user store sees it.</summary>
/// <param name="TwoFactorEnabled">
/// Read by <c>LoginUseCase</c> to decide whether a verified password is enough to issue tokens or a
/// second-factor challenge has to come first — and by
/// <c>SignInWithExternalProviderUseCase</c> for the same decision, which is what stops linking a
/// provider identity from being the way around the second factor. Both readers are security
/// decisions, so an adapter that projected this from anything but the user's own flag would disarm
/// the factor rather than misreport it.
/// </param>
public sealed record AccountIdentity(Guid UserId, string UserName, string Email, bool TwoFactorEnabled);
