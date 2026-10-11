namespace AppTemplate.Api.Common.Security;

/// <summary>Builds the rate limiter's partition key for a request, prefixed by its kind
/// (<c>ip:</c>) so a future key of another kind cannot collide into this one's partition.</summary>
/// <remarks><b>Every rate-limit policy partitions on the client address</b>, so callers sharing an
/// address share a budget — a budget per user is not available even where the template authenticates,
/// because the limiter runs before <c>UseAuthentication</c> and <see cref="HttpContext.User"/> is
/// the anonymous default. Moving authentication earlier would fix the key and break something worse:
/// validating a bearer costs a signature check at minimum, plus a never-cached security-stamp read
/// with local accounts, so every request the limiter was about to reject would first pay that cost —
/// an amplifier of the very load it should be shedding.</remarks>
internal static class RateLimiterPartitionKeys
{
    private const string _addressPrefix = "ip:";

    public static string ForAddress(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return _addressPrefix + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
