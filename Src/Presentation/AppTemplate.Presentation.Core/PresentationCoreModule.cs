using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Presentation.Core.Common.Localization;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Presentation.Core;

/// <summary>
/// What a host composes from this project besides observability and outbound HTTP, each of which is
/// a call of its own because a host may want either without the other.
/// </summary>
public static class PresentationCoreModule
{
    /// <summary>
    /// Binds and validates <see cref="LocalizationOptions"/>.
    /// <para>
    /// Binding only. Reading the bound value into <c>CurrentLanguage.Default</c> is left to the
    /// host, because when that happens is a host's own question: one with a request pipeline does it
    /// as the pipeline is built, one with only background work does it once the container exists and
    /// before the first loop runs. Doing it here would pick for both.
    /// </para>
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddLocalizationOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<LocalizationOptions>()
            .BindConfiguration(LocalizationOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();

        return services;
    }

    /// <summary>
    /// Registers the identity of a host that has no caller, so a use case reaching for one fails
    /// where it was composed rather than deep in a loop.
    /// <para>
    /// Scoped, matching the lifetime an HTTP host gives its own caller: the same use case is
    /// resolved from a scope in either, and a singleton here would be a difference in the graph for
    /// no reason.
    /// </para>
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNoCallerIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICurrentUser, NoCallerCurrentUser>();

        return services;
    }
}
