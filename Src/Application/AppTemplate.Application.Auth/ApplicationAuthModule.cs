using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Core;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Application.Auth;

public static class ApplicationAuthModule
{
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssemblyContaining<LoginCommandValidator>(
            lifetime: ServiceLifetime.Scoped,
            includeInternalTypes: true);

        services.AddUseCasesFrom(typeof(ApplicationAuthModule).Assembly);

        return services;
    }
}
