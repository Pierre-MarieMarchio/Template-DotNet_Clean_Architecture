namespace AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;

/// <summary>
/// An account's passkeys: the ceremonies that register one, the list its owner reads, and the one
/// a sign-in presents. The WebAuthn arithmetic — challenges, origins, signatures — is the
/// adapter's behind this port; the decisions live in the use cases: what a refusal answers,
/// whether the account may still sign in, what the security log records. A ceremony is two calls,
/// and the state that joins them is an opaque string handed back to the caller — the same shape
/// <c>ITwoFactorService</c> gives a challenge token; no ceremony state is held server-side. Every
/// write commits on its own, and the sign-in step updates the credential's signature counter
/// <em>before</em> it returns, so a caller that mints tokens next reads a credential whose replay
/// window has closed.
/// </summary>
public interface IPasskeyService
{
    /// <summary>
    /// Starts a registration: the creation options the client feeds to
    /// <c>navigator.credentials.create()</c> and the state string that joins the two calls.
    /// Credentials this account already holds are excluded, so an authenticator offers a new one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No account has that id.</exception>
    Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a registration: verifies the ceremony against the state
    /// <see cref="BeginRegistrationAsync"/> issued, then stores the credential. The state names the
    /// ceremony's account and must match <paramref name="userId"/>, so a state one caller obtained
    /// cannot register a credential on another's account.
    /// </summary>
    Task<PasskeyRegistrationOutcome> CompleteRegistrationAsync(
        Guid userId,
        string credentialJson,
        string registrationState,
        string? name,
        CancellationToken cancellationToken = default);

    /// <summary>The account's registered passkeys, the newest first, for their owner to read.</summary>
    /// <returns>An empty list when the account no longer exists: nothing about a vanished account
    /// is there to list.</returns>
    Task<IReadOnlyList<PasskeyCredential>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the account's passkeys. A credential already removed answers <c>false</c>
    /// again, exactly as one the account never held — the caller is not told which. Commits before
    /// it returns.
    /// </summary>
    Task<bool> RemoveAsync(
        Guid userId,
        byte[] credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a sign-in with no account named: builds the request options the client feeds to
    /// <c>navigator.credentials.get()</c> for an authenticator to offer a discoverable credential,
    /// and the state string that joins the two calls.
    /// </summary>
    Task<PasskeySignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a sign-in: verifies the ceremony against the state <see cref="BeginSignInAsync"/>
    /// issued, identifies the account through the credential's user handle, and persists the
    /// credential's new signature counter before returning. Nothing is issued here: whether the
    /// account may still sign in and what tokens it gets is the use case's to decide.
    /// </summary>
    Task<PasskeySignInOutcome> CompleteSignInAsync(
        string credentialJson,
        string authenticationState,
        CancellationToken cancellationToken = default);
}
