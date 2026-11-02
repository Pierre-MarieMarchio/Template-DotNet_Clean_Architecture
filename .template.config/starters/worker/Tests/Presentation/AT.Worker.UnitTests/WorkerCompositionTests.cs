using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Presentation.Core.Common.Jobs;
using AppTemplate.Worker.Common.Security;
using AppTemplate.Worker.UnitTests.TestSupport;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests;

[Collection(WorkerHostCollectionDefinition.Name)]
public sealed class WorkerCompositionTests
{
    [Fact]
    public void TheWorker_BuildsUnderDevelopmentsStrictValidation()
    {
        using var worker = WorkerHost.Build();

        worker.Services.GetRequiredService<IHostEnvironment>().IsDevelopment().ShouldBeTrue();
        worker.UseCases.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task EveryUseCaseTheWorkerComposes_ResolvesInABackgroundScope()
    {
        using var worker = WorkerHost.Build();

        foreach (var useCase in worker.UseCases)
        {
            await using var scope = worker.Services.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

            scope.ServiceProvider.GetService(useCase).ShouldNotBeNull($"{useCase.Name} is registered and does not resolve.");
        }
    }

    [Fact]
    public async Task ABackgroundScope_HasNoCaller_AndRecordsNobody()
    {
        using var worker = WorkerHost.Build();

        await using var scope = worker.Services.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

        Should.Throw<NotSupportedException>(() => scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId);
        scope.ServiceProvider.GetRequiredService<IAuditActor>().UserId.ShouldBeNull();
    }

    [Fact]
    public void TheWorker_RunsTheJobs()
    {
        using var worker = WorkerHost.Build();

        JobsOf(worker).ShouldContain("MaintenanceBackgroundService");
    }

    [Fact]
    public void TheWorker_ServesNoHttp()
    {
        using var worker = WorkerHost.Build();

        worker.Host.Services.GetService<IServiceProviderIsService>()!
            .IsService(Type.GetType("Microsoft.AspNetCore.Hosting.Server.IServer, Microsoft.AspNetCore.Hosting.Server.Abstractions") ?? typeof(void))
            .ShouldBeFalse("The worker runs the jobs and serves nothing: a server in it is an HTTP surface no route needs.");
    }

    [Fact]
    public void JobsSwitchedOff_LeaveTheWorkerWithNoLoop()
    {
        using var worker = WorkerHost.Build(new Dictionary<string, string?> { ["Jobs:Enabled"] = "false" });

        JobsOf(worker).ShouldBeEmpty();
    }

    private static List<string> JobsOf(WorkerHost worker) =>
        [.. worker.Services.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .Where(type => type.Assembly == typeof(WorkerCallerIdentity).Assembly)
            .Select(type => type.Name)];
}
