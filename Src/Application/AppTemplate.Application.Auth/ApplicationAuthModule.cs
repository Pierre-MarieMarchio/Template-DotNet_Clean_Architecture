using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Core;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Application.Auth;

/// <summary>
/// What a host composes to offer accounts and sign-in.
/// <para>
/// One call, and a host that does not make this call has no authentication use cases registered at
/// all — which is the point: this is the project a derived application is most likely to replace
/// wholesale with its own identity provider, and it can only be dropped if nothing else composes
/// it on its behalf.
/// </para>
/// <para>
/// Dropping it is not free, and the cost is not in this project. An identity module has to satisfy
/// the twenty ports under <c>Features/Auth/Ports/</c>; the persistence context in this template
/// derives from ASP.NET Identity's own; and a mail module whose reminder notifier resolves a user
/// profile to find an address needs one of these ports even when nothing signs in. Removing
/// authentication is a decision about those three, not about this call.
/// </para>
/// </summary>
public static class ApplicationAuthModule
{
    /// <summary>
    /// Registers every use case and every validator this project declares.
    /// <para>
    /// Discovered from this assembly rather than listed: the assembly holds nothing but
    /// authentication, so its own boundary is the filter, and there is no list to fall behind the
    /// folder it describes.
    /// </para>
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
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
