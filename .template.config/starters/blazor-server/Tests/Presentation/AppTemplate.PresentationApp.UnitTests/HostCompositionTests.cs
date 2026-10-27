using System.Security.Claims;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.PresentationApp.Common.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests;

public sealed class HostCompositionTests
{
    private static readonly Guid _caller = Guid.CreateVersion7();

    [Fact]
    public void TheHostContainer_BuildsUnderDevelopmentsStrictValidation()
    {
        using var host = new PresentationHost();
        using var circuit = host.Services.CreateScope();

        circuit.ServiceProvider.GetRequiredService<OperationScope>().ShouldNotBeNull();
    }

    [Fact]
    public async Task EveryUseCaseTheHostComposes_ResolvesInAnOperationScope()
    {
        using var host = new PresentationHost(SignedIn(_caller));
        using var circuit = host.Services.CreateScope();
        var operations = circuit.ServiceProvider.GetRequiredService<OperationScope>();
        foreach (var useCase in host.UseCases)
        {
            (await operations.RunAsync<IServiceProvider, object?>(scope => Task.FromResult(scope.GetService(useCase))))
                .ShouldNotBeNull($"{useCase.Name} is registered and does not resolve.");
        }
    }

    [Fact]
    public async Task AnOperationScope_GivesItsUseCasesTheCircuitsCaller()
    {
        using var host = new PresentationHost(SignedIn(_caller));
        using var circuit = host.Services.CreateScope();

        var seen = await circuit.ServiceProvider.GetRequiredService<OperationScope>()
            .RunAsync<ICurrentUser, Guid?>(caller => Task.FromResult(caller.UserId?.Value));

        seen.ShouldBe(_caller);
    }

    [Fact]
    public void AUseCaseResolvedOutsideARequestAndAnOperationScope_RefusesToGuessItsCaller()
    {
        using var host = new PresentationHost(SignedIn(_caller));
        using var scope = host.Services.CreateScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId)
            .Message.ShouldContain(nameof(OperationScope));
    }

    [Fact]
    public void TheHost_RunsNoBackgroundWorkOfItsOwn()
    {
        using var host = new PresentationHost();

        host.Services.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .Where(type => type.Assembly.GetName().Name!.StartsWith("AppTemplate.", StringComparison.Ordinal))
            .ShouldBeEmpty("Jobs run in the API under a lease; a second host that ran them too would run every pass twice.");
    }

    private static ClaimsPrincipal SignedIn(Guid userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
}
