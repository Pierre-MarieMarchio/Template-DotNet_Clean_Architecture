using Scalar.AspNetCore;

namespace AppTemplate.Api.Common.Security;

internal static class ApiReferenceContentSecurityPolicy
{
    internal const string PathPrefix = "/scalar";

    internal static string? For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return IsApiReference(context.Request.Path) ? Policy(NonceOf(context)) : null;
    }

    private static string Policy(string? nonce)
    {
        string scriptSource = string.IsNullOrEmpty(nonce) ? "'self'" : $"'self' 'nonce-{nonce}'";

        return "default-src 'none'; " +
            $"script-src {scriptSource}; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src https://fonts.scalar.com; " +
            "connect-src 'self'; " +
            "worker-src 'self' blob:; " +
            "frame-ancestors 'none'; " +
            "base-uri 'none'; " +
            "form-action 'none'";
    }

    private static string? NonceOf(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ScalarOptions.NonceHttpContextItemKey, out object? nonce)
            ? nonce as string
            : null;

    private static bool IsApiReference(PathString path) =>
        path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);
}
