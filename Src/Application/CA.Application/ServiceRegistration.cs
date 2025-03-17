using CA.Application.Commons.Interfaces.UseCase;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace CA.Application;

public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.RegisterValidators();
        services.RegisterUseCase();
        services.RegisterManagers();
        return services;
    }

    private static void RegisterValidators(this IServiceCollection services)
    {
        var validatorBaseType = typeof(AbstractValidator<>);
        var assembly = Assembly.GetExecutingAssembly();

        var validatorTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.BaseType != null
                        && t.BaseType.IsGenericType
                        && t.BaseType.GetGenericTypeDefinition() == validatorBaseType)
            .ToList();

        foreach (var validatorType in validatorTypes)
        {
            var modelType = validatorType.BaseType!.GetGenericArguments()[0];
            var interfaceType = typeof(IValidator<>).MakeGenericType(modelType);

            services.AddScoped(interfaceType, validatorType);
        }
    }

    private static void RegisterUseCase(this IServiceCollection services)
    {
        var baseInterfaceTypes = new[] { typeof(IBaseUseCase<,>), typeof(IBaseUseCase<>) };
        var assembly = Assembly.GetExecutingAssembly();

        var useCaseInterfaces = assembly.GetTypes()
            .Where(t => t.IsInterface && t.GetInterfaces()
                .Any(i => i.IsGenericType && baseInterfaceTypes.Contains(i.GetGenericTypeDefinition())));

        var useCaseImplementations = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract &&
                        useCaseInterfaces.Any(i => i.IsAssignableFrom(t)));

        foreach (var implementation in useCaseImplementations)
        {
            var interfaceType = useCaseInterfaces.FirstOrDefault(i => i.IsAssignableFrom(implementation));

            if (interfaceType != null)
            {
                services.AddScoped(interfaceType, implementation);
            }
        }
    }

    private static void RegisterManagers(this IServiceCollection services)
    {
        var managerInterfaces = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsInterface && t.Name.StartsWith("I") && t.Name.EndsWith("Manager"))
            .ToList();

        foreach (var managerInterface in managerInterfaces)
        {
            var implementationType = Assembly.GetExecutingAssembly()
                .GetTypes()
                .FirstOrDefault(t => t.GetInterfaces().Contains(managerInterface) && t.Name == managerInterface.Name[1..]);

            if (implementationType != null)
            {
                services.AddScoped(managerInterface, implementationType);
            }
        }
    }
}
