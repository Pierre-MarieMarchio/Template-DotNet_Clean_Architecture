using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

internal sealed class TestClientAddressStartupFilter : IStartupFilter
{
    internal const string HeaderName = "X-Integration-Test-Client-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (HttpContext context, RequestDelegate nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var values)
                    && IPAddress.TryParse(values.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }
}
