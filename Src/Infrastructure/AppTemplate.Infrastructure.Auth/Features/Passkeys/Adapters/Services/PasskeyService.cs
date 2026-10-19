using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Adapters.Services;

/// <summary><see cref="IPasskeyService"/> over ASP.NET Core Identity's own passkey support: the
/// ceremony arithmetic in <see cref="IPasskeyHandler{TUser}"/>, the credential rows in the user
/// store through <see cref="UserManager{TUser}"/>. The handler is used directly rather than through
/// <c>SignInManager</c>'s passkey methods — those park the ceremony state in a cookie, and this API
/// keeps none: the state string returned to the caller is the same bytes the cookie would have held.
/// Each store write commits on its own: a sign-in's new signature counter is durable before the use
/// case mints any token, so the replay window closes first. Concurrent registrations of the same
/// credential are settled by the ceremony — the second of two racing attempts is told it did not
/// verify — and concurrent removals both read it, one removes, the second is told <c>false</c>, the
/// same answer an unknown id gets.</summary>
internal sealed class PasskeyService(
    UserManager<AppUser> userManager,
    IPasskeyHandler<AppUser> passkeyHandler,
    IHttpContextAccessor httpContextAccessor,
    IAppUserDirectory directory) : IPasskeyService
{
    /// <summary>Checked before the handler is reached, because the ceremony needs the request's
    /// origin and host and a host with no request has neither.</summary>
    private HttpContext Context => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("A passkey ceremony needs an HTTP request.");

    /// <inheritdoc />
    public async Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account has the id '{userId}'.");

        // The user handle an authenticator stores: the account's id, so a discoverable credential
        // names it without an address having to travel. The address is the name an authenticator
        // shows, and the display name what it puts beside it.
        var challenge = await passkeyHandler.MakeCreationOptionsAsync(
            new PasskeyUserEntity
            {
                Id = user.Id.ToString(),
                Name = user.Email ?? string.Empty,
                DisplayName = user.UserName ?? string.Empty,
            },
            Context);

        return new PasskeyRegistrationChallenge(
            challenge.CreationOptionsJson,
            challenge.AttestationState
                ?? throw new InvalidOperationException("The passkey handler issued no attestation state."));
    }

    /// <inheritdoc />
    public async Task<PasskeyRegistrationOutcome> CompleteRegistrationAsync(
        Guid userId,
        string credentialJson,
        string registrationState,
        string? name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var attestation = await passkeyHandler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = Context,
            CredentialJson = credentialJson,
            AttestationState = registrationState,
        });

        if (!attestation.Succeeded)
        {
            return PasskeyRegistrationOutcome.InvalidCeremony();
        }

        // The state was issued naming this account's handle; a state naming another's was not
        // issued to this caller, and completing it would store a credential whose user handle
        // answers a different account.
        if (!string.Equals(attestation.UserEntity.Id, userId.ToString(), StringComparison.Ordinal))
        {
            return PasskeyRegistrationOutcome.InvalidCeremony();
        }

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return PasskeyRegistrationOutcome.NoSuchAccount();
        }

        attestation.Passkey.Name = name;

        var stored = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);

        return stored.Succeeded
            ? PasskeyRegistrationOutcome.Registered(new PasskeyCredential(
                attestation.Passkey.CredentialId,
                attestation.Passkey.Name,
                attestation.Passkey.CreatedAt))
            : PasskeyRegistrationOutcome.StoreRejected();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasskeyCredential>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return [];
        }

        var passkeys = await userManager.GetPasskeysAsync(user);

        return
        [
            .. passkeys
                .Select(passkey => new PasskeyCredential(passkey.CredentialId, passkey.Name, passkey.CreatedAt))
                .OrderByDescending(passkey => passkey.CreatedAt),
        ];
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        Guid userId,
        byte[] credentialId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        // Read before remove, so an id this account never held answers as one of somebody else's:
        // the caller of the port is not told which, and neither is its caller.
        if (await userManager.GetPasskeyAsync(user, credentialId) is null)
        {
            return false;
        }

        return (await userManager.RemovePasskeyAsync(user, credentialId)).Succeeded;
    }

    /// <inheritdoc />
    public async Task<PasskeySignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No user is named: the authenticator offers a discoverable credential, and the account it
        // names is the one the ceremony identifies through the credential's user handle.
        var challenge = await passkeyHandler.MakeRequestOptionsAsync(user: null, Context);

        return new PasskeySignInChallenge(
            challenge.RequestOptionsJson,
            challenge.AssertionState
                ?? throw new InvalidOperationException("The passkey handler issued no assertion state."));
    }

    /// <inheritdoc />
    public async Task<PasskeySignInOutcome> CompleteSignInAsync(
        string credentialJson,
        string authenticationState,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var assertion = await passkeyHandler.PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = Context,
            CredentialJson = credentialJson,
            AssertionState = authenticationState,
        });

        if (!assertion.Succeeded)
        {
            return PasskeySignInOutcome.InvalidCeremony();
        }

        // The counter before the account facts: a caller that mints tokens next must be reading a
        // credential whose replay window has already closed. A refusal here stops the sign-in
        // rather than completing it against a credential that could be replayed.
        var persisted = await userManager.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);

        if (!persisted.Succeeded)
        {
            return PasskeySignInOutcome.StoreRejected();
        }

        return PasskeySignInOutcome.Authenticated(new AccountIdentity(
            assertion.User.Id,
            assertion.User.UserName!,
            assertion.User.Email!,
            assertion.User.TwoFactorEnabled));
    }
}
