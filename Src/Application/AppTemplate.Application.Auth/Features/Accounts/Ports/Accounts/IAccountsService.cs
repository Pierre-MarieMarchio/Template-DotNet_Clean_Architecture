namespace AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

/// <summary>The account itself: creating one, checking its password, saying whether it may still
/// sign in, changing its password, reading its profile, and deleting it. Nothing about tokens or
/// mail. <b>Cancellation is observed on entry only:</b> ASP.NET Identity offers no way to pass a
/// <see cref="CancellationToken"/> into <c>UserManager</c> or <c>SignInManager</c>, so the parameter
/// stays for the entry check, not because the work is interruptible. Every other authentication
/// port behaves the same way.</summary>
public interface IAccountsService
{
    /// <summary>
    /// Creates an account with a password. The address is stored unconfirmed: proving it is a
    /// separate step, through <c>IEmailTokensService</c>.
    /// </summary>
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

    /// <summary>Verifies the current password and, on a match, replaces it. Identity rotates the
    /// security stamp as part of this call, which invalidates every access token already issued for
    /// the account through <c>ValidateSecurityStampAsync</c>; refresh tokens are untouched and are
    /// the caller's to revoke.</summary>
    Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the account's current profile from the store, never from the bearer token's
    /// claims — a claim is only as fresh as the access token that carries it.</summary>
    /// <returns><c>null</c> when the account no longer exists.</returns>
    Task<UserProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the account and what the user store holds for it, its refresh-token grants included.
    /// The authority is the caller's to prove — re-authentication for the holder's own hands, the
    /// <c>Administrator</c> policy for an administrator's — and the port says nothing about it.
    /// Commits on its own, like every Identity-backed write, so the fact of the deletion can be
    /// published the moment this returns. Rows another feature owns under this id are not touched:
    /// those are the owning feature's to remove, through the account-deleted fact.
    /// </summary>
    Task<AccountDeletionOutcome> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
