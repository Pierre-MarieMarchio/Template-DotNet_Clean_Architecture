namespace AppTemplate.Api.Common.Security;

internal static class RateLimiterPartitionKeys
{
    private const string _addressPrefix = "ip:";

    public static string ForAddress(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return _addressPrefix + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
