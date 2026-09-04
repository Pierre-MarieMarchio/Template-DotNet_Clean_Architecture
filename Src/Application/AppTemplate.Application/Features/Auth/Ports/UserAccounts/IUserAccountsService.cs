namespace AppTemplate.Application.Features.Auth.Ports.UserAccounts;

/// <summary>
/// The account itself: creating one, checking a credential against one, and saying whether one may
/// still sign in. Nothing about tokens or mail.
/// <para>
/// <b>Cancellation is observed on entry only, and cannot be propagated.</b> An implementation over
/// ASP.NET Identity has no way to pass a <see cref="CancellationToken"/> into
/// <c>UserManager</c> or <c>SignInManager</c>, so a token cancelled while one of these calls is in
/// flight does not stop it. The parameter is kept because these calls sit on a request's path and
/// the check at entry is worth having, not because the work is interruptible.
/// </para>
/// </summary>
public interface IUserAccountsService
{
    Task<AccountCreationOutcome> CreateAsync(
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a password. Implementations must cost the same whether or not the address exists,
    /// because the response time is otherwise an oracle for which addresses are registered, and must
    /// count the failure towards lockout.
    /// </summary>
    Task<CredentialCheckOutcome> VerifyCredentialAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>Whether an already-authenticated principal is still allowed to obtain a new token.</summary>
    Task<bool> CanSignInAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the current password and, on a match, replaces it. ASP.NET Identity rotates the
    /// security stamp as part of this call, which — through the bearer handler's
    /// <c>ValidateSecurityStampAsync</c> — invalidates every access token already issued for the
    /// account. Refresh tokens are untouched by that rotation and are the caller's responsibility to
    /// revoke.
    /// </summary>
    Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}
