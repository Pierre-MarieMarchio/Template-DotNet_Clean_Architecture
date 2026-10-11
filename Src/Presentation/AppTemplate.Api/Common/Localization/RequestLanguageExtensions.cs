using System.Globalization;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Presentation.Core;
using AppTemplate.Presentation.Core.Common.Localization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Localization;

/// <summary>Reads the caller's language off the request and makes it the ambient one, so a mail
/// this request causes is written in the language the caller reads rather than the one the server
/// was deployed in.</summary>
/// <remarks>Deliberately not <c>UseRequestLocalization</c>: it negotiates against a list of
/// supported cultures this template does not have (what is supported is which templates the
/// infrastructure modules embed), and it works in <c>CultureInfo</c>, which this repository builds
/// without — see <see cref="CurrentLanguage"/> and <c>InvariantGlobalization</c>. The header is
/// believed rather than validated: a tag naming a language nothing was written in falls back inside
/// the renderer that knows which languages exist.</remarks>
internal static class RequestLanguageExtensions
{
    /// <summary>
    /// Longer than any real <c>Accept-Language</c>, and short enough that a caller cannot make this
    /// middleware do work by sending a large one.
    /// </summary>
    private const int _maximumHeaderLength = 256;

    /// <summary>Binds and validates <see cref="LocalizationOptions"/> at start-up.</summary>
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

    /// <summary>
    /// The well-formed tag the caller weighs highest (RFC 9110 section 12.5.4): a missing <c>q</c> is
    /// 1, <c>q=0</c> means "not acceptable" and excludes the tag, and equal weights keep the order the
    /// caller wrote them in. An entry with a malformed tag or an invalid <c>q</c> is skipped whole. A
    /// mail is written in one language, so only the winner is kept.
    /// </summary>
    /// <remarks>
    /// Parsed by hand rather than with <c>StringWithQualityHeaderValue</c>: that parser resynchronises
    /// inside a bad entry and hands back fragments of it (<c>"not a tag"</c> yields <c>"tag"</c>),
    /// which would turn a malformed tag into a language.
    /// </remarks>
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
                // Strictly greater: on a tie the earlier entry stays.
                best = tag;
                bestWeight = weight;
            }
        }

        return best;
    }

    /// <summary>
    /// 1 when no <c>q</c> parameter is given; otherwise the value of <c>q</c>, which must be a number
    /// from 0 to 1. Other parameters are ignored.
    /// </summary>
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
