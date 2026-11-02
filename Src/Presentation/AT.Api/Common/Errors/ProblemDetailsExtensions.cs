using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Errors;

internal static class ProblemDetailsExtensions
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ProblemTypeOptions>()
            .BindConfiguration(ProblemTypeOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ProblemTypeOptions>, ProblemTypeOptionsValidator>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            ProblemDetailsNormaliser.Normalise(context.ProblemDetails, context.HttpContext));

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
