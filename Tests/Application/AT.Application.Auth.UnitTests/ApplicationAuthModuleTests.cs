using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests;

public sealed class ApplicationAuthModuleTests
{
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

    private static IEnumerable<Type> ValidatorContracts() =>
        typeof(ApplicationAuthModule).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, BaseType.IsGenericType: true }
                && type.BaseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            .Select(type => typeof(IValidator<>).MakeGenericType(type.BaseType!.GetGenericArguments()));
}
