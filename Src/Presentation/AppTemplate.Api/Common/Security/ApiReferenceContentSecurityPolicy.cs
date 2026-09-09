using Scalar.AspNetCore;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// The one path this host serves that a JSON API's default-deny policy would leave blank, and the
/// policy it needs instead.
/// </summary>
internal static class ApiReferenceContentSecurityPolicy
{
    /// <summary>Where the API-reference page and its own assets are served.</summary>
    internal const string PathPrefix = "/scalar";

    /// <summary>
    /// Answers the reference page's policy on its own paths, and <see langword="null"/> everywhere
    /// else, which leaves the configured API policy in place.
    /// </summary>
    internal static string? For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return IsApiReference(context.Request.Path) ? Policy(NonceOf(context)) : null;
    }

    /// <summary>
    /// What the API-reference page needs, and nothing beyond it. Each directive answers something the
    /// shipped <c>Scalar.AspNetCore</c> bundle actually does:
    /// <list type="bullet">
    /// <item><c>script-src</c>: two same-origin files under the prefix, plus one inline
    /// <c>&lt;script type="module"&gt;</c> that the nonce covers.</item>
    /// <item><c>style-src 'unsafe-inline'</c>: the bundle mounts its stylesheet by creating a
    /// <c>&lt;style&gt;</c> element and assigning <c>textContent</c>, and its components carry
    /// <c>style</c> attributes. Neither can be nonced from here.</item>
    /// <item><c>img-src</c>: the served <c>favicon.svg</c>, <c>data:</c> images inlined in the
    /// bundle's CSS, and <c>blob:</c> previews of a response body.</item>
    /// <item><c>font-src</c>: the bundle's <c>@font-face</c> rules load Inter from
    /// <c>fonts.scalar.com</c>. Declaring the host is honest about what the page fetches; hiding it
    /// by turning the fonts off would be changing the page to fit the policy.</item>
    /// <item><c>connect-src 'self'</c>: it fetches the OpenAPI document, and "try it" posts to this
    /// same origin.</item>
    /// <item><c>worker-src</c>: it starts a module worker from an object URL.</item>
    /// </list>
    /// </summary>
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

    /// <summary>
    /// The nonce <c>WithNonce()</c> generated for this request. It is written while the endpoint runs,
    /// which is after the header middleware but before the response starts — which is why the headers
    /// are written from <c>OnStarting</c> and why this is read from there too.
    /// </summary>
    private static string? NonceOf(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ScalarOptions.NonceHttpContextItemKey, out object? nonce)
            ? nonce as string
            : null;

    private static bool IsApiReference(PathString path) =>
        path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);
}
