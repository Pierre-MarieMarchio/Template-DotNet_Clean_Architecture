using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Api.IntegrationTests.Auth;

internal static class AuthenticatorCodes
{
    private static readonly MethodInfo _fromBase32 = typeof(IdentityOptions).Assembly
        .GetType("Microsoft.AspNetCore.Identity.Base32", throwOnError: true)!
        .GetMethod("FromBase32", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo _computeTotp = typeof(IdentityOptions).Assembly
        .GetType("Microsoft.AspNetCore.Identity.Rfc6238AuthenticationService", throwOnError: true)!
        .GetMethod("ComputeTotp", BindingFlags.NonPublic | BindingFlags.Static)!;

    public static string CurrentCodeFor(string base32SharedKey)
    {
        byte[] key = (byte[])_fromBase32.Invoke(null, [base32SharedKey])!;
        ulong timestep = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        int code = (int)_computeTotp.Invoke(null, [key, timestep, null])!;

        return code.ToString("D6", CultureInfo.InvariantCulture);
    }
}
