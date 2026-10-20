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
    internal const string RegistrationStatePurpose = "AppTemplate.Infrastructure.Auth.Passkeys.RegistrationState";

    internal const string SignInStatePurpose = "AppTemplate.Infrastructure.Auth.Passkeys.SignInState";

    private static readonly TimeSpan _clockSkewAllowance = TimeSpan.FromMinutes(5);

    private HttpContext Context => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("A passkey ceremony needs an HTTP request.");

    public async Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"No account has the id '{userId}'.");

        var challenge = await passkeyHandler.MakeCreationOptionsAsync(
            new PasskeyUserEntity
            {
                Id = user.Id.ToString(),
                Name = user.Email ?? string.Empty,
                DisplayName = user.Email ?? string.Empty,
            },
            Context);

        string state = challenge.AttestationState
            ?? throw new InvalidOperationException("The passkey handler issued no attestation state.");

        await PurgeSpentCeremoniesAsync(cancellationToken);

        return new PasskeyRegistrationChallenge(
            challenge.CreationOptionsJson,
            Seal(RegistrationProtector(userId), state));
    }

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

        if (await userManager.GetPasskeyAsync(user, credentialId) is null)
        {
            return false;
        }

        if (!(await userManager.RemovePasskeyAsync(user, credentialId)).Succeeded)
        {
            return false;
        }

        await userManager.UpdateSecurityStampAsync(user);

        return true;
    }

    public async Task<PasskeySignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var challenge = await passkeyHandler.MakeRequestOptionsAsync(user: null, Context);

        string state = challenge.AssertionState
            ?? throw new InvalidOperationException("The passkey handler issued no assertion state.");

        await PurgeSpentCeremoniesAsync(cancellationToken);

        return new PasskeySignInChallenge(challenge.RequestOptionsJson, Seal(SignInProtector(), state));
    }

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

        var persisted = await userManager.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);

        if (!persisted.Succeeded)
        {
            return PasskeySignInOutcome.StoreRejected();
        }

        return PasskeySignInOutcome.Authenticated(new AccountIdentity(
            assertion.User.Id,
            assertion.User.Email!,
            assertion.User.TwoFactorEnabled));
    }

    private ITimeLimitedDataProtector RegistrationProtector(Guid userId) =>
        dataProtectionProvider
            .CreateProtector(RegistrationStatePurpose, userId.ToString("N"))
            .ToTimeLimitedDataProtector();

    private ITimeLimitedDataProtector SignInProtector() =>
        dataProtectionProvider.CreateProtector(SignInStatePurpose).ToTimeLimitedDataProtector();

    private string Seal(ITimeLimitedDataProtector protector, string state) =>
        protector.Protect(state, dateTimeProvider.UtcNow.Add(options.Value.CeremonyLifetime));

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

        if (expiresAt <= dateTimeProvider.UtcNow)
        {
            return null;
        }

        return await spentCeremonies.TrySpendAsync(Hash(state), expiresAt, cancellationToken)
            ? state
            : null;
    }

    private Task<int> PurgeSpentCeremoniesAsync(CancellationToken cancellationToken) =>
        spentCeremonies.PurgeExpiredAsync(dateTimeProvider.UtcNow.Subtract(_clockSkewAllowance), cancellationToken);

    private static string Hash(string state) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
