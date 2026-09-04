using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// Registers the framework's <c>ProblemDetails</c> service and points it at
/// <see cref="ProblemDetailsNormaliser"/>, which is what makes the normalisation apply to the
/// responses the framework writes on its own, before any of our code runs.
/// </summary>
public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Binds and validates <see cref="ProblemTypeOptions"/> at startup rather than on first use: a
    /// misconfigured <c>type</c> base URI is a deployment mistake, and it should stop the host
    /// instead of surfacing on the first request that happens to fail.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ProblemTypeOptions>()
            .BindConfiguration(ProblemTypeOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ProblemTypeOptions>, ProblemTypeOptionsValidator>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            ProblemDetailsNormaliser.Normalise(context.ProblemDetails, context.HttpContext));

        return services;
    }
}
