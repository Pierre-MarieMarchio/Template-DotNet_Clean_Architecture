using System.Collections.Concurrent;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// Identity's own passkey handler for the options a ceremony starts with, and a verifier that
/// accepts every ceremony it is handed: the credential JSON is taken to be the id of the account
/// to answer for, which holds the one passkey <see cref="CredentialId"/> names.
/// </summary>
/// <remarks>
/// This suite has no software authenticator, so it cannot sign a real assertion. What it can do is
/// make verification succeed unconditionally — and then a second completion refused is refused by
/// the state's single use and by nothing else, since the handler would have accepted it. Every state
/// the handler is asked to verify is recorded, so a test can tell "refused before the handler" from
/// "refused by it".
/// </remarks>
internal sealed class AcceptingPasskeyHandler(
    UserManager<AppUser> userManager,
    IOptions<IdentityPasskeyOptions> options,
    AcceptingPasskeyHandler.Ledger ledger) : IPasskeyHandler<AppUser>
{
    /// <summary>The credential every account answered for holds.</summary>
    internal static readonly byte[] CredentialId = [7, 7, 7, 7, 7, 7, 7, 7];

    private readonly PasskeyHandler<AppUser> _identity = new(userManager, options);

    public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(
        PasskeyUserEntity userEntity,
        HttpContext httpContext) =>
        _identity.MakeCreationOptionsAsync(userEntity, httpContext);

    public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(AppUser? user, HttpContext httpContext) =>
        _identity.MakeRequestOptionsAsync(user, httpContext);

    public async Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ledger.Verified.Enqueue(context.AttestationState!);

        var user = await userManager.FindByIdAsync(context.CredentialJson)
            ?? throw new InvalidOperationException($"No account has the id '{context.CredentialJson}'.");

        // A fresh credential id per registration, so the store never refuses a second one as a
        // duplicate: a refusal here must come from the state alone.
        return PasskeyAttestationResult.Success(
            APasskey(Guid.CreateVersion7().ToByteArray()),
            new PasskeyUserEntity { Id = user.Id.ToString(), Name = user.Email!, DisplayName = user.UserName! });
    }

    public async Task<PasskeyAssertionResult<AppUser>> PerformAssertionAsync(PasskeyAssertionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ledger.Verified.Enqueue(context.AssertionState!);

        var user = await userManager.FindByIdAsync(context.CredentialJson)
            ?? throw new InvalidOperationException($"No account has the id '{context.CredentialJson}'.");

        // The counter a synced passkey reports: zero, every time. Identity's own check would let
        // this through, so nothing but the state's single use stands between a replay and a session.
        return PasskeyAssertionResult.Success(APasskey(CredentialId), user);
    }

    internal static UserPasskeyInfo APasskey(byte[] credentialId) => new(
        credentialId,
        publicKey: [4, 5, 6],
        createdAt: DateTimeOffset.UtcNow,
        signCount: 0,
        transports: [],
        isUserVerified: true,
        isBackupEligible: true,
        isBackedUp: true,
        attestationObject: [],
        clientDataJson: [])
    {
        Name = "Synced passkey",
    };

    /// <summary>Every state the handler was asked to verify, in the order it was asked.</summary>
    internal sealed class Ledger
    {
        public ConcurrentQueue<string> Verified { get; } = new();
    }
}
