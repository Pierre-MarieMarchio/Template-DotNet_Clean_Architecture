using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace AppTemplate.Presentation.Core.Common.Outbound;

public static class OutboundHttpExtensions
{
    public static IServiceCollection AddOutboundHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);

            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = DelayBackoffType.Exponential;

            options.Retry.UseJitter = true;

            var isTransientFailure = options.Retry.ShouldHandle;

            options.Retry.ShouldHandle = args =>
                IsSafeToReplay(args.Context.GetRequestMessage()?.Method)
                    ? isTransientFailure(args)
                    : PredicateResult.False();
        }));

        return services;
    }

    private static bool IsSafeToReplay(HttpMethod? method) =>
        method is not null
        && (method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Options
            || method == HttpMethod.Trace);
}
