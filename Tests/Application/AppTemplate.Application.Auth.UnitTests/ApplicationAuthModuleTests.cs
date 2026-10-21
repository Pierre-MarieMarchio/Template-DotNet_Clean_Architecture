using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests;

public sealed class ApplicationAuthModuleTests
{
    [Fact]
    public void EveryUseCase_IsBoundOnceAndScopedThroughItsOwnInterface()
    {
        var services = new ServiceCollection();
        services.AddAuthApplication();

        UseCaseDiscovery.Implementations.ShouldNotBeEmpty();

        string[] offenders =
        [
            .. UseCaseDiscovery.Implementations
                .Where(implementation => !IsBoundOnceThroughItsInterface(services, implementation))
                .Select(implementation => implementation.FullName!),
        ];

        offenders.ShouldBeEmpty(
            "A use case reaches a controller through its own interface, scoped, from one registration; " +
            "these do not.");
    }

    [Fact]
    public void EveryValidator_IsRegisteredOnceAndScoped()
    {
        var services = new ServiceCollection();
        services.AddAuthApplication();

        Type[] contracts = [.. ValidatorContracts()];

        contracts.ShouldNotBeEmpty();

        string[] offenders =
        [
            .. contracts
                .Where(contract => services.Where(descriptor => descriptor.ServiceType == contract).ToList()
                    is not [{ Lifetime: ServiceLifetime.Scoped }])
                .Select(contract => contract.ToString()),
        ];

        offenders.ShouldBeEmpty(
            "A validator the module does not register once is a rule the request is never held to.");
    }

    private static bool IsBoundOnceThroughItsInterface(ServiceCollection services, Type implementation) =>
        services.Where(descriptor => descriptor.ServiceType == UseCaseDiscovery.ContractOf(implementation)).ToList()
            is [{ Lifetime: ServiceLifetime.Scoped } descriptor]
        && descriptor.ImplementationType == implementation
        && !services.Any(other => other.ServiceType == implementation);

    private static IEnumerable<Type> ValidatorContracts() =>
        typeof(ApplicationAuthModule).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, BaseType.IsGenericType: true }
                && type.BaseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            .Select(type => typeof(IValidator<>).MakeGenericType(type.BaseType!.GetGenericArguments()));
}
