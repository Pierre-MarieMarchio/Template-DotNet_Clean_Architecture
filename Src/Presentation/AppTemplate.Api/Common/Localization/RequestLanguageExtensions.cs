using System.Globalization;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Presentation.Core;
using AppTemplate.Presentation.Core.Common.Localization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Localization;

internal static class RequestLanguageExtensions
{
    private const int _maximumHeaderLength = 256;

    public static IServiceCollection AddRequestLanguage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddLocalizationOptions();
    }

    public static IApplicationBuilder UseRequestLanguage(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        CurrentLanguage.Default = app.ApplicationServices
            .GetRequiredService<IOptions<LocalizationOptions>>().Value.DefaultCulture;

        return app.Use(async (context, next) =>
        {
            CurrentLanguage.Tag = Preferred(context.Request.Headers.AcceptLanguage);

            await next(context);
        });
    }

    private static string? Preferred(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage) || acceptLanguage.Length > _maximumHeaderLength)
        {
            return null;
        }

        string? best = null;
        double bestWeight = 0;

        foreach (string entry in acceptLanguage.Split(','))
        {
            string[] parts = entry.Split(';');
            string tag = parts[0].Trim();

            if (CurrentLanguage.IsWellFormed(tag)
                && TryReadWeight(parts.AsSpan(1), out double weight)
                && weight > bestWeight)
            {
                best = tag;
                bestWeight = weight;
            }
        }

        return best;
    }

    private static bool TryReadWeight(ReadOnlySpan<string> parameters, out double weight)
    {
        weight = 1;

        foreach (string parameter in parameters)
        {
            int equals = parameter.IndexOf('=', StringComparison.Ordinal);

            if (equals < 0 || !parameter.AsSpan(0, equals).Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return double.TryParse(
                    parameter.AsSpan(equals + 1).Trim(),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out weight)
                && weight is >= 0 and <= 1;
        }

        return true;
    }
}
