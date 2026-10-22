using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Adapters.Services;

internal sealed class PasskeyCeremonyStateIssuer(
    IDataProtectionProvider dataProtectionProvider,
    ISpentPasskeyCeremonyTable spentCeremonies,
    IDateTimeProvider dateTimeProvider,
    IOptions<PasskeyOptions> options)
{
    internal const string RegistrationStatePurpose = "AppTemplate.Infrastructure.Auth.Passkeys.RegistrationState";

    internal const string SignInStatePurpose = "AppTemplate.Infrastructure.Auth.Passkeys.SignInState";

    private static readonly TimeSpan _clockSkewAllowance = TimeSpan.FromMinutes(5);

    public string SealRegistrationState(Guid userId, string state) => Seal(RegistrationProtector(userId), state);

    public string SealSignInState(string state) => Seal(SignInProtector(), state);

    public Task<string?> SpendRegistrationStateAsync(
        Guid userId,
        string sealedState,
        CancellationToken cancellationToken) =>
        SpendAsync(RegistrationProtector(userId), sealedState, cancellationToken);

    public Task<string?> SpendSignInStateAsync(string sealedState, CancellationToken cancellationToken) =>
        SpendAsync(SignInProtector(), sealedState, cancellationToken);

    public Task<int> PurgeSpentAsync(CancellationToken cancellationToken) =>
        spentCeremonies.PurgeExpiredAsync(dateTimeProvider.UtcNow.Subtract(_clockSkewAllowance), cancellationToken);

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

    private static string Hash(string state) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
