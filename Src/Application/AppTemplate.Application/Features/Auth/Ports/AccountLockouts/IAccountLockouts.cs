namespace AppTemplate.Application.Features.Auth.Ports.AccountLockouts;

/// <summary>
/// Administrative lockout: suspending sign-in indefinitely and lifting that suspension, both outside
/// the automatic, timed lockout <see cref="Ports.UserAccounts.IUserAccounts.VerifyCredentialAsync"/>
/// already applies after too many failed attempts.
/// </summary>
public interface IAccountLockouts
{
    /// <summary>
    /// Suspends sign-in until <see cref="UnlockAsync"/> is called. Unlike the automatic lockout, this
    /// carries no expiry: an administrator who locks an account decides when it ends, not a timer.
    /// </summary>
    Task<LockoutChangeOutcome> LockAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Lifts an administrative lockout. A no-op, not a failure, on an account that was not locked.</summary>
    Task<LockoutChangeOutcome> UnlockAsync(Guid userId, CancellationToken cancellationToken = default);
}
