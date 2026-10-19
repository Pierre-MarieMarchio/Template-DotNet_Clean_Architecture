using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Adapters.Services;

/// <summary><see cref="IPasskeyService"/> over ASP.NET Core Identity's own passkey support: the
/// ceremony arithmetic in <see cref="IPasskeyHandler{TUser}"/>, the credential rows in the user
/// store through <see cref="UserManager{TUser}"/>. The handler is used directly rather than through
/// <c>SignInManager</c>'s passkey methods — those park the ceremony state in a cookie, and this API
/// sets none, so the state travels to the caller as a string instead.
/// <para>
/// That string is the handler's state sealed by a time-limited data protector, under a purpose of
/// its own for each ceremony — a registration's also names the account — so it cannot be read,
/// rewritten, carried from one ceremony kind to the other, or presented after
/// <see cref="PasskeyOptions.CeremonyLifetime"/>. And it is spent on its first presentation:
/// <see cref="ISpentPasskeyCeremonyTable"/> records it <em>before</em> the handler sees it, so a
/// state is used up even by a ceremony that then fails to verify, and of two presentations —
/// simultaneous or not, on one instance or several — at most one reaches the handler. Without
/// that, a captured state and credential would sign in again and again from an authenticator that
/// keeps no signature counter, as synced passkeys do. Every refusal of a state answers exactly as a
/// ceremony that did not verify.
/// </para>
/// Each store write commits on its own: a sign-in's new signature counter is durable before the use
/// case mints any token, so the replay window closes first. Concurrent registrations of the same
/// credential are settled by the ceremony — the second of two racing attempts is told it did not
/// verify — and concurrent removals both read it, one removes, the second is told <c>false</c>, the
/// same answer an unknown id gets.</summary>
internal sealed class PasskeyService(
    UserManager<AppUser> userManager,
    IPasskeyHandler<AppUser> passkeyHandler,
    IHttpContextAccessor httpContextAccessor,
    IAppUserDirectory directory,
    IDataProtectionProvider dataProtectionProvider,
    ISpentPasskeyCeremonyTable spentCeremonies,
    IDateTimeProvider dateTimeProvider,
    IOptions<PasskeyOptions> options) : IPasskeyService
{
    /// <summary>The data-protection purpose a registration state is sealed under, beside the
    /// account's id.</summary>
    internal const string RegistrationStatePurpose = "AppTemplate.Infrastructure.Auth.Passkeys.RegistrationState";

    /// <summary>The data-protection purpose a sign-in state is sealed under.</summary>
    internal const string SignInStatePurpose = "AppTemplate.Infrastructure.Auth.Passkeys.SignInState";

    /// <summary>
    /// How long a spent record outlives its state. The state's expiry is read against the clock of
    /// whichever instance it is presented to, so a record purged the moment it expires on one
    /// instance would leave the state redeemable once more on an instance whose clock runs behind.
    /// </summary>
    private static readonly TimeSpan _clockSkewAllowance = TimeSpan.FromMinutes(5);

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

        string state = challenge.AttestationState
            ?? throw new InvalidOperationException("The passkey handler issued no attestation state.");

        await PurgeSpentCeremoniesAsync(cancellationToken);

        return new PasskeyRegistrationChallenge(
            challenge.CreationOptionsJson,
            Seal(RegistrationProtector(userId), state));
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

        string? state = await SpendAsync(RegistrationProtector(userId), registrationState, cancellationToken);

        if (state is null)
        {
            return PasskeyRegistrationOutcome.InvalidCeremony();
        }

        var attestation = await passkeyHandler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = Context,
            CredentialJson = credentialJson,
            AttestationState = state,
        });

        if (!attestation.Succeeded)
        {
            return PasskeyRegistrationOutcome.InvalidCeremony();
        }

        // The state was issued naming this account's handle; a state naming another's was not
        // issued to this caller, and completing it would store a credential whose user handle
        // answers a different account. The protector's purpose already names the account, so this
        // is the second of two checks rather than the only one.
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

        if (!(await userManager.RemovePasskeyAsync(user, credentialId)).Succeeded)
        {
            return false;
        }

        // Identity does not treat removing a passkey as a credential change, unlike a password
        // change, so without this the access tokens already in circulation keep validating until
        // they expire.
        await userManager.UpdateSecurityStampAsync(user);

        return true;
    }

    /// <inheritdoc />
    public async Task<PasskeySignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No user is named: the authenticator offers a discoverable credential, and the account it
        // names is the one the ceremony identifies through the credential's user handle.
        var challenge = await passkeyHandler.MakeRequestOptionsAsync(user: null, Context);

        string state = challenge.AssertionState
            ?? throw new InvalidOperationException("The passkey handler issued no assertion state.");

        await PurgeSpentCeremoniesAsync(cancellationToken);

        return new PasskeySignInChallenge(challenge.RequestOptionsJson, Seal(SignInProtector(), state));
    }

    /// <inheritdoc />
    public async Task<PasskeySignInOutcome> CompleteSignInAsync(
        string credentialJson,
        string authenticationState,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? state = await SpendAsync(SignInProtector(), authenticationState, cancellationToken);

        if (state is null)
        {
            return PasskeySignInOutcome.InvalidCeremony();
        }

        var assertion = await passkeyHandler.PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = Context,
            CredentialJson = credentialJson,
            AssertionState = state,
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

    /// <summary>A registration's state is sealed for the one account it was issued to, so another
    /// account's caller cannot even spend it.</summary>
    private ITimeLimitedDataProtector RegistrationProtector(Guid userId) =>
        dataProtectionProvider
            .CreateProtector(RegistrationStatePurpose, userId.ToString("N"))
            .ToTimeLimitedDataProtector();

    private ITimeLimitedDataProtector SignInProtector() =>
        dataProtectionProvider.CreateProtector(SignInStatePurpose).ToTimeLimitedDataProtector();

    private string Seal(ITimeLimitedDataProtector protector, string state) =>
        protector.Protect(state, dateTimeProvider.UtcNow.Add(options.Value.CeremonyLifetime));

    /// <summary>
    /// Unseals a presented state and spends it, in that order and before any verification.
    /// </summary>
    /// <returns>The handler's own state, or <c>null</c> for a state this purpose never sealed, one
    /// that was rewritten, one that has expired, and one already spent — four cases the caller is
    /// not told apart, exactly as a ceremony that does not verify.</returns>
    private async Task<string?> SpendAsync(
        ITimeLimitedDataProtector protector,
        string sealedState,
        CancellationToken cancellationToken)
    {
        string state;
        DateTimeOffset expiresAt;

        try
        {
            state = protector.Unprotect(sealedState, out expiresAt);
        }
        catch (CryptographicException)
        {
            return null;
        }

        // The protector checks the expiry against the machine's clock; this checks it against the
        // module's, which is the one every other expiry here is read from.
        if (expiresAt <= dateTimeProvider.UtcNow)
        {
            return null;
        }

        return await spentCeremonies.TrySpendAsync(Hash(state), expiresAt, cancellationToken)
            ? state
            : null;
    }

    /// <summary>
    /// Lease-free and idempotent, like every purge here, and run where the table grows rather than
    /// on a timer: the records live a few minutes, so a sweep beside each ceremony keeps the table
    /// at a few minutes of traffic with no job to configure.
    /// </summary>
    private Task<int> PurgeSpentCeremoniesAsync(CancellationToken cancellationToken) =>
        spentCeremonies.PurgeExpiredAsync(dateTimeProvider.UtcNow.Subtract(_clockSkewAllowance), cancellationToken);

    /// <summary>The unsealed state carries the ceremony's random challenge, so its hash names that
    /// ceremony and no other; the state itself is never stored.</summary>
    private static string Hash(string state) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
