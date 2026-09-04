using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Lets a test choose the client address the host sees, by sending it in a header.
/// </summary>
/// <remarks>
/// <para>
/// Rate limiting partitions on <c>HttpContext.Connection.RemoteIpAddress</c>. Under
/// <c>TestServer</c> that address is null for every request, so the whole suite would share a
/// single ten-requests-per-minute budget on the authentication endpoints and tests would start
/// failing with 429 for reasons that have nothing to do with what they assert. Handing each test
/// its own address gives each its own partition, and leaves the limiter itself — the thing the
/// rate-limiting test is about — completely untouched.
/// </para>
/// <para>
/// An <see cref="IStartupFilter"/> rather than a middleware registered in the host, because the
/// address has to be in place before <c>UseRateLimiter</c> runs and this is the only hook that gets
/// in front of a pipeline the test cannot edit. A single filter's middleware is outermost.
/// </para>
/// </remarks>
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
