namespace AppTemplate.Application.Features.Auth.Ports.TwoFactorAdministration;

/// <summary>
/// Stripping a second factor from someone else's account, on an administrator's own authority rather
/// than that account's password.
/// <para>
/// Deliberately not <c>ITwoFactorEnrollment</c>: that capability is reached by the account itself and
/// every operation on it is gated by a credential only the account holder has — see
/// <c>ITwoFactorEnrollment.DisableAsync</c>. This one exists for exactly the case that gate cannot
/// serve: a caller who has lost the authenticator app <em>and</em> the recovery codes, and so can
/// prove neither a code nor — once the second factor is armed — a completed login at all. Without it,
/// the only recourse left is <c>IAccountDeletion</c>, which is a much larger hammer than the second
/// factor being stuck on.
/// </para>
/// </summary>
public interface ITwoFactorAdministration
{
    /// <summary>
    /// Turns two-factor sign-in off unconditionally — no password, no code, no proof of the sort
    /// <c>ITwoFactorEnrollment.DisableAsync</c> demands, because an administrator reaching this
    /// capability has already proven who they are through the <c>Administrator</c> policy, not
    /// through the target account's own credential. A no-op, not a failure, on an account that
    /// already has it off.
    /// </summary>
    Task<TwoFactorAdministrativeDisableOutcome> DisableAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
