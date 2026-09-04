namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;

/// <summary>
/// An account's own two-factor enrollment, end to end: provisioning a secret, confirming it with a
/// first code, and turning the whole thing back off.
/// <para>
/// Deliberately not the login-time challenge — see <c>ITwoFactorChallenge</c>. That capability is
/// reached by an anonymous caller mid sign-in and never touches whether two-factor sign-in is armed
/// in the first place; this one is reached by an already-authenticated caller managing their own
/// account and never issues a token. Different callers, different lifecycles, and combining them
/// would be the five-operation façade <c>PortConventionTests</c> refuses.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only, and cannot be propagated</b>, for the reason given on
/// <c>IUserAccounts</c>.
/// </para>
/// </summary>
public interface ITwoFactorEnrollment
{
    /// <summary>
    /// Provisions a secret if none is pending yet, and returns it either way — a second call before
    /// confirmation hands back the same one rather than silently replacing it, so reloading the setup
    /// page does not invalidate a code the caller already scanned.
    /// </summary>
    Task<TwoFactorSetup> BeginAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the current password and the first code against the pending secret and, on a match
    /// against both, turns two-factor sign-in on and mints a fresh set of recovery codes. Two-factor
    /// sign-in is never armed by <see cref="BeginAsync"/> alone — only a confirmed code proves the
    /// caller can actually produce one, without which they would be locked out of their own account
    /// the moment it turned on. The password is required for the same reason <see cref="DisableAsync"/>
    /// gives: arming the second factor revokes every refresh token for the account exactly as
    /// disarming it does, so a caller who could not disable it with a stolen session alone must not be
    /// able to arm it with one either.
    /// </summary>
    Task<TwoFactorConfirmation> ConfirmAsync(
        Guid userId,
        string currentPassword,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the current password and, on a match, turns two-factor sign-in back off. The
    /// password is required for the reason <c>IUserAccounts.ChangePasswordAsync</c> gives: a stolen
    /// session alone must not be able to strip the account's second factor.
    /// </summary>
    Task<TwoFactorDisable> DisableAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default);
}
