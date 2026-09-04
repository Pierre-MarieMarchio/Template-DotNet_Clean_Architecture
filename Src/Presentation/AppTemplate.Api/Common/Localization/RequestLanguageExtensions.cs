using AppTemplate.Application.Common.Localization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Localization;

/// <summary>
/// Reads the caller's language off the request and makes it the ambient one, so that a mail this
/// request causes is written in the language the caller reads rather than the one the server was
/// deployed in.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>UseRequestLocalization</c>, for two reasons. It negotiates against a list of
/// supported cultures, and this template has no such list to give it — what is supported is which
/// templates the infrastructure modules embed, and handing it a second list is how the two drift.
/// And it works in <c>CultureInfo</c>, which this repository builds without: see
/// <see cref="CurrentLanguage"/> and <c>InvariantGlobalization</c> in <c>Directory.Build.props</c>.
/// </para>
/// <para>
/// The header is believed rather than validated against anything but its own shape: a tag naming a
/// language nothing was written in falls back on its own, inside the renderer that knows which
/// languages exist.
/// </para>
/// </remarks>
public static class RequestLanguageExtensions
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

        services.AddOptions<LocalizationOptions>()
            .BindConfiguration(LocalizationOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();

        return services;
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
    /// The first well-formed tag the caller lists, in the order they listed it. Quality values are
    /// not honoured: a mail is written in one language, so the first tag is the answer, and ordering
    /// by <c>q</c> would only change which of several a caller gets when they asked for the first.
    /// </summary>
    private static string? Preferred(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage) || acceptLanguage.Length > _maximumHeaderLength)
        {
            return null;
        }

        foreach (string entry in acceptLanguage.Split(','))
        {
            string tag = entry.Split(';')[0].Trim();

            if (CurrentLanguage.IsWellFormed(tag))
            {
                return tag;
            }
        }

        return null;
    }
}
