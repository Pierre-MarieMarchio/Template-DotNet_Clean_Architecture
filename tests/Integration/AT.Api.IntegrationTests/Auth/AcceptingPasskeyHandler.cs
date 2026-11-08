using System.Collections.Concurrent;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.IntegrationTests.Auth;

internal sealed class AcceptingPasskeyHandler(
    UserManager<AppUser> userManager,
    IOptions<IdentityPasskeyOptions> options,
    AcceptingPasskeyHandler.Ledger ledger) : IPasskeyHandler<AppUser>
{
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

        return PasskeyAttestationResult.Success(
            APasskey(Guid.CreateVersion7().ToByteArray()),
            new PasskeyUserEntity { Id = user.Id.ToString(), Name = user.Email!, DisplayName = user.Email! });
    }

    public async Task<PasskeyAssertionResult<AppUser>> PerformAssertionAsync(PasskeyAssertionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ledger.Verified.Enqueue(context.AssertionState!);

        var user = await userManager.FindByIdAsync(context.CredentialJson)
            ?? throw new InvalidOperationException($"No account has the id '{context.CredentialJson}'.");

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

    internal sealed class Ledger
    {
        public ConcurrentQueue<string> Verified { get; } = new();
    }
}
