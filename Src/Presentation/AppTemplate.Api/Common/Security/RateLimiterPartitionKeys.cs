namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Builds the rate limiter's partition key for a request. The key is prefixed by its kind
/// (<c>ip:</c>) so that a future key of another kind cannot collide into this one's partition.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both policies partition on the client address, including for authenticated callers</b>, so
/// callers sharing an address share a budget. The tempting alternative — a budget per authenticated
/// user — is not available here: a partition key is computed where the limiter runs, and the limiter
/// runs before <c>UseAuthentication</c>, so <see cref="HttpContext.User"/> is the anonymous default
/// for every request whatever bearer token it carries.
/// </para>
/// <para>
/// Moving authentication earlier would fix the key and break something worse. Validating a bearer
/// costs a signature check <i>and</i> a database read of the security stamp, which is never cached,
/// so every request the limiter was about to reject would first pay for a database round trip. A
/// limiter exists to refuse traffic early and cheaply; under the volumetric attack it is there to
/// absorb, that ordering turns it into an amplifier of the very load it should be shedding.
/// </para>
/// <para>
/// This matters twice over for the <c>authentication</c> policy, which would be wrong to partition
/// by user even if identity were available: it exists to slow down credential discovery on endpoints
/// where, by construction, there is no identity yet.
/// </para>
/// </remarks>
internal static class RateLimiterPartitionKeys
{
    private const string _addressPrefix = "ip:";

    public static string ForAddress(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return _addressPrefix + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
