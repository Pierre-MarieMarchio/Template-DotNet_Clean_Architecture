using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// Computes the six-digit code a real authenticator app would show for a given shared key, so a test
/// can drive the real <c>/two-factor/confirm</c> and <c>/login/two-factor</c> endpoints without a
/// human ever typing a code in.
/// <para>
/// Reached through reflection rather than a call, because <c>Rfc6238AuthenticationService</c> —
/// exactly the type <c>AuthenticatorTokenProvider{TUser}.ValidateAsync</c> calls in production — is
/// <c>internal</c> to the framework assembly. This invokes that same compiled method rather than
/// re-deriving RFC 6238 by hand: a duplicate implementation could drift from the framework's and this
/// suite would stop proving anything about the real endpoint.
/// </para>
/// </summary>
internal static class AuthenticatorCodes
{
    private static readonly MethodInfo _fromBase32 = typeof(IdentityOptions).Assembly
        .GetType("Microsoft.AspNetCore.Identity.Base32", throwOnError: true)!
        .GetMethod("FromBase32", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo _computeTotp = typeof(IdentityOptions).Assembly
        .GetType("Microsoft.AspNetCore.Identity.Rfc6238AuthenticationService", throwOnError: true)!
        .GetMethod("ComputeTotp", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// The code valid right now for <paramref name="base32SharedKey"/>: a 30-second time step and no
    /// modifier, exactly what <c>AuthenticatorTokenProvider{TUser}.ValidateAsync</c> checks against.
    /// </summary>
    public static string CurrentCodeFor(string base32SharedKey)
    {
        byte[] key = (byte[])_fromBase32.Invoke(null, [base32SharedKey])!;
        ulong timestep = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        int code = (int)_computeTotp.Invoke(null, [key, timestep, null])!;

        return code.ToString("D6", CultureInfo.InvariantCulture);
    }
}
