namespace AppTemplate.Api.Common.Caching;

internal static class CacheHeaderExtensions
{
    private const string _readDefault = "private, no-cache";
    private const string _noStore = "no-store";

    private const string _varyValue = "Authorization, Origin";

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
