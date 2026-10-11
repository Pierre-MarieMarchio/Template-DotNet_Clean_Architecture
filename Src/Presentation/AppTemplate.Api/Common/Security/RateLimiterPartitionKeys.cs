namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Builds the rate limiter's partition key for a request. The key is prefixed by its kind
/// (<c>ip:</c>) so that a future key of another kind cannot collide into this one's partition.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every rate-limit policy partitions on the client address</b>, so callers sharing an address
/// share a budget. A budget per authenticated user is not available even where the template
/// authenticates: a partition key is computed where the limiter runs, and the limiter runs before
/// <c>UseAuthentication</c>, so <see cref="HttpContext.User"/> is the anonymous default for every
/// request whatever bearer token it carries.
/// </para>
/// <para>
/// Moving authentication earlier would fix the key and break something worse. Validating a bearer
/// costs a signature check at minimum, and with local accounts a database read of the never-cached
/// security stamp besides, so every request the limiter was about to reject would first pay that
/// cost. A limiter exists to refuse traffic early and cheaply; under the volumetric attack it is
/// there to absorb, that ordering turns it into an amplifier of the very load it should be shedding.
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
