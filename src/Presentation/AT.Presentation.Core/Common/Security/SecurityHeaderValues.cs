namespace AppTemplate.Presentation.Core.Common.Security;

public static class SecurityHeaderValues
{
    public const string ContentSecurityPolicyHeader = "Content-Security-Policy";

    public const string PoweredByHeader = "X-Powered-By";

    public static IReadOnlyDictionary<string, string> Fixed { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["Referrer-Policy"] = "no-referrer",
        };
}
