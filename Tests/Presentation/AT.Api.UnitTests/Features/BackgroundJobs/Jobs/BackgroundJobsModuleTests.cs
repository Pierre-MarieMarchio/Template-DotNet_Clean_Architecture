//#if (localAuth)
using AppTemplate.Api.Features.Auth.Jobs;
//#endif
using AppTemplate.Api.Features.BackgroundJobs.Jobs;
//#if (files)
using AppTemplate.Api.Features.Files.Jobs;
//#endif
using AppTemplate.Api.Features.Maintenance.Jobs;
//#if (reminders)
using AppTemplate.Api.Features.Reminders.Jobs;
//#endif
//#if (localAuth)
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
//#endif
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
//#if (files)
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
//#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;

public sealed class BackgroundJobsModuleTests
{
    private static readonly ServiceProviderOptions _strict = new() { ValidateOnBuild = true, ValidateScopes = true };

    [Fact]
    public void Enabled_IsTheDefault_AndStartsEveryJob_UnderStrictContainerValidation()
    {
        var services = Compose([]);

        using var provider = services.BuildServiceProvider(_strict);

        var hosted = provider.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        hosted.ShouldContain(typeof(MaintenanceBackgroundService));
        //#if (reminders)
        hosted.ShouldContain(typeof(ReminderBackgroundService));
        //#endif
        //#if (files)
        hosted.ShouldContain(typeof(FileBackgroundService));
        //#endif
    }

    [Fact]
    public void Disabled_StartsNoJob_AndBindsNoJobOption()
    {
        var services = Compose(new Dictionary<string, string?> { ["Jobs:Enabled"] = "false" });

        using var provider = services.BuildServiceProvider(_strict);

        provider.GetServices<IHostedService>().ShouldBeEmpty();

        Type[] jobValidators =
        [
            typeof(IValidateOptions<JobsMaintenanceOptions>),
            //#if (reminders)
            typeof(IValidateOptions<JobsRemindersOptions>),
            //#endif
            //#if (files)
            typeof(IValidateOptions<JobsFilesOptions>),
            //#endif
        ];

        services.ShouldNotContain(
            descriptor => jobValidators.Contains(descriptor.ServiceType),
            "an instance that only serves HTTP must not be made to validate settings it never reads");
    }

    [Fact]
    public async Task Disabled_StartsWithJobSettingsThatWouldFailValidation()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Jobs:Enabled"] = "false",
            ["Jobs:Maintenance:Interval"] = "00:00:00",
            //#if (reminders)
            ["Jobs:Reminders:Interval"] = "7.00:00:00",
            //#endif
            //#if (files)
            ["Jobs:Files:InspectDepositedFilesInterval"] = "00:00:01",
            //#endif
        });

        await Should.NotThrowAsync(() => host.StartAsync(TestContext.Current.CancellationToken));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Jobs:Maintenance:Interval", "00:00:00")]
    //#if (reminders)
    [InlineData("Jobs:Reminders:Interval", "7.00:00:00")]
    //#endif
    //#if (files)
    [InlineData("Jobs:Files:InspectDepositedFilesInterval", "00:00:01")]
    [InlineData("Jobs:Files:ReclaimOrphanedContentInterval", "00:00:05")]
    [InlineData("Jobs:Files:PurgeAbandonedRegistrationsInterval", "00:00:00")]
    //#endif
    public async Task Enabled_RefusesToStart_OnAnInvalidJobSetting(string key, string value)
    {
        using var host = BuildHost(new Dictionary<string, string?> { [key] = value });

        var failure = await Should.ThrowAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(key);
    }

    [Fact]
    /*#if (localAuth)*/
    public async Task Enabled_RunsOnePassOfEveryJob_IncludingAuthenticationsPurge()
    /*#else
    public async Task Enabled_RunsOnePassOfEveryJob()
    #endif*/
    {
        var idempotency = Substitute.For<IPurgeExpiredIdempotencyKeysUseCase>();
        //#if (localAuth)
        var refreshTokens = Substitute.For<IPurgeExpiredRefreshTokensUseCase>();
        var securityEvents = Substitute.For<IPurgeExpiredSecurityEventsUseCase>();
        //#endif
        //#if (reminders)
        var reminders = Substitute.For<IFireDueRemindersUseCase>();
        //#endif
        //#if (files)
        var abandoned = Substitute.For<IPurgeAbandonedRegistrationsUseCase>();
        var orphaned = Substitute.For<IReclaimOrphanedContentUseCase>();
        var inspection = Substitute.For<IInspectDepositedFilesUseCase>();
        //#endif

        idempotency.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        //#if (localAuth)
        refreshTokens.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        securityEvents.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        //#endif
        //#if (reminders)
        reminders.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        //#endif
        //#if (files)
        abandoned.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        orphaned.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        inspection.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        //#endif

        using var host = BuildHost(
            new Dictionary<string, string?>(),
            services =>
            {
                services.AddScoped(_ => idempotency);
                //#if (localAuth)
                services.AddScoped(_ => refreshTokens);
                services.AddScoped(_ => securityEvents);
                //#endif
                //#if (reminders)
                services.AddScoped(_ => reminders);
                //#endif
                //#if (files)
                services.AddScoped(_ => abandoned);
                services.AddScoped(_ => orphaned);
                services.AddScoped(_ => inspection);
                //#endif
            });

        await host.StartAsync(TestContext.Current.CancellationToken);

        Func<bool>[] passes =
        [
            () => Received(idempotency),
            //#if (localAuth)
            () => Received(refreshTokens),
            () => Received(securityEvents),
            //#endif
            //#if (reminders)
            () => Received(reminders),
            //#endif
            //#if (files)
            () => Received(abandoned),
            () => Received(orphaned),
            () => Received(inspection),
            //#endif
        ];

        await BackgroundServiceProbe.WaitUntilAsync(
            () => passes.All(pass => pass()),
            "every job to have run its first pass");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private static bool Received<TUseCase>(TUseCase useCase)
        where TUseCase : class =>
        useCase.ReceivedCalls().Any();

    private static ServiceCollection Compose(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBackgroundJobs(configuration);
        //#if (localAuth)
        services.AddAuthMaintenanceJobs(configuration);
        //#endif

        return services;
    }

    private static IHost BuildHost(Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddLogging();
        builder.Services.AddBackgroundJobs(builder.Configuration);
        //#if (localAuth)
        builder.Services.AddAuthMaintenanceJobs(builder.Configuration);
        //#endif
        configure?.Invoke(builder.Services);

        return builder.Build();
    }
}
