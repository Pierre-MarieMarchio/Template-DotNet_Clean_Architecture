namespace AppTemplate.Api.Common.Caching;

/// <summary>
/// States whether a response may be stored at all, so every action carries an explicit caching
/// contract instead of leaving one implied.
/// </summary>
/// <remarks>
/// See <c>docs/adr/0019-caching-is-revalidation-not-storage.md</c>: <c>private</c> confines storage to
/// the end client, never a shared cache, because every read here is scoped to the caller's own rows.
/// <c>no-cache</c> still permits that client to store the response, but requires it to revalidate with
/// the origin before reuse — which is precisely what the strong <c>ETag</c> / <c>If-None-Match</c> /
/// <c>304</c> flow this API already publishes exists to make cheap. Neither directive is a promise that
/// every cache in existence behaves; a cache that ignores <c>private</c> was already untrustworthy with
/// or without this header.
/// <para>
/// An action carrying <see cref="NoStoreAttribute"/> gets <c>no-store</c> instead, on any method —
/// a token response is not a read, so it never reached the default below, and RFC 6749 §5.1 requires
/// that directive regardless.
/// </para>
/// </remarks>
public static class CacheHeaderExtensions
{
    private const string _readDefault = "private, no-cache";
    private const string _noStore = "no-store";

    // Every read here is scoped to the caller's bearer token, and CORS is active, so a shared cache
    // keyed only on the URL could serve one caller's — or one origin's — response to another.
    private const string _varyValue = "Authorization, Origin";

    /// <summary>
    /// Install early, for the same reason as <see cref="Security.SecurityHeadersExtensions"/>:
    /// <c>UseExceptionHandler</c> calls <c>Response.Clear()</c> before it re-runs the pipeline, so a
    /// header set on the way in would be dropped from exactly the error responses that most need it.
    /// Registering through <see cref="HttpResponse.OnStarting(Func{object, Task}, object)"/> survives
    /// that reset because the header is written only once the response is actually about to begin.
    /// </summary>
    public static WebApplication UseApiCacheHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use((context, next) =>
        {
            context.Response.OnStarting(
                static state =>
                {
                    var httpContext = (HttpContext)state;
                    var response = httpContext.Response;

                    // Never overwrite one already set: a future endpoint that knows better than this
                    // blanket default must win.
                    if (response.Headers.CacheControl.Count == 0)
                    {
                        bool noStore = httpContext.GetEndpoint()?.Metadata.GetMetadata<NoStoreAttribute>() is not null;

                        if (noStore)
                        {
                            response.Headers.CacheControl = _noStore;
                        }
                        else if (IsRead(httpContext.Request.Method))
                        {
                            response.Headers.CacheControl = _readDefault;
                        }
                    }

                    if (response.Headers.Vary.Count == 0)
                    {
                        response.Headers.Vary = _varyValue;
                    }

                    return Task.CompletedTask;
                },
                context);

            return next(context);
        });

        return app;
    }

    private static bool IsRead(string method) => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
}
