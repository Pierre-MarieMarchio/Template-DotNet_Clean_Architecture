using System.Reflection;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.PresentationAppMcp.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppMcp.UnitTests;

public sealed class HostCompositionTests
{
    [Fact]
    public async Task TheHostContainer_BuildsUnderDevelopmentsStrictValidation()
    {
        await using var host = new McpHost();
        using var request = host.Services.CreateScope();

        request.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.ShouldBeNull(
            "Outside a request there is no caller, and a use case refuses to guess one.");
    }

    [Fact]
    public async Task EveryUseCaseTheHostComposes_ResolvesInARequestScope()
    {
        await using var host = new McpHost();
        using var request = host.Services.CreateScope();

        foreach (var useCase in host.UseCases)
        {
            request.ServiceProvider.GetService(useCase).ShouldNotBeNull($"{useCase.Name} is registered and does not resolve.");
        }
    }

    [Fact]
    public async Task EveryToolType_ResolvesTheUseCasesItCalls()
    {
        await using var host = new McpHost();
        using var request = host.Services.CreateScope();
        Type[] toolTypes = [.. typeof(Program).Assembly.GetTypes().Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)];

        foreach (var toolType in toolTypes)
        {
            ActivatorUtilities.CreateInstance(request.ServiceProvider, toolType).ShouldNotBeNull(
                $"{toolType.Name} declares tools whose use cases this host does not compose.");
        }

        toolTypes
            .SelectMany(type => type.GetMethods())
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ShouldBe(ToolCatalogue.Names, ignoreOrder: true, "Each tool is declared by hand, and the catalogue the tests read lists it.");
    }

    [Fact]
    public async Task TheHost_RunsNoBackgroundWorkOfItsOwn()
    {
        await using var host = new McpHost();

        host.Services.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .Where(type => type.Assembly.GetName().Name!.StartsWith("AppTemplate.", StringComparison.Ordinal))
            .ShouldBeEmpty("Jobs run in one host under a lease; a second host that ran them too would run every pass twice.");
    }
}
