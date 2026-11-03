using Microsoft.AspNetCore.Authorization;

namespace AppTemplate.PresentationAppPortal.Common.Security;

internal static class AnonymousPages
{
    internal static readonly string[] Routes = ["/Error", "/not-found"];

    internal static TBuilder AllowAnonymousPages<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint =>
        {
            if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: { } route }
                && Routes.Contains(route, StringComparer.OrdinalIgnoreCase))
            {
                endpoint.Metadata.Add(new AllowAnonymousAttribute());
            }
        });

        return builder;
    }
}
