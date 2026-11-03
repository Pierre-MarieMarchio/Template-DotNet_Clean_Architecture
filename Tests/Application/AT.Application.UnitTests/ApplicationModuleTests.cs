//#if (files)
using AppTemplate.Application.Features.Files.Configuration.Options;
//#endif
//#if (files && todoLists)
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
//#endif
//#if (reminders && todoLists)
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
//#endif
//#if (todoLists)
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
//#endif
using FluentValidation;
//#if (files)
using Microsoft.Extensions.Configuration;
//#endif
using Microsoft.Extensions.DependencyInjection;
//#if (files)
using Microsoft.Extensions.Options;
//#endif
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests;

public sealed class ApplicationModuleTests
{
    [Fact]
    public void EveryValidator_IsRegisteredOnceAndScoped()
    {
        var services = ComposeEveryFeature();

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
            "A validator the feature calls do not register once is a rule the request is never held to.");
    }
    //#if (todoLists)

    [Fact]
    public void AFeatureCall_RegistersItsOwnVerticalAndNoOther()
    {
        var services = new ServiceCollection();
        services.AddTodoLists();

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IValidator<CreateTodoListCommand>));
        //#if (reminders)
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IValidator<ScheduleReminderCommand>));
        //#endif
        //#if (files)
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IValidator<RegisterFileCommand>));
        //#endif
    }
    //#endif
    //#if (files)

    [Fact]
    public void TheFilesSection_IsValidatedAtStart()
    {
        var services = new ServiceCollection();
        services.AddFiles(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void TheFilesSection_BindsItsCategories()
    {
        var services = new ServiceCollection();
        services.AddFiles(FilesConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var category = provider.GetRequiredService<IOptions<FilesOptions>>().Value.Categories["document"];
        category.AllowedMediaTypes.ShouldBe(["application/pdf"]);
        category.MaxSizeInBytes.ShouldBe(1_048_576);
        category.UploadModes.ShouldBe(["Direct"]);
        category.Quota.MaxFiles.ShouldBe(1_000);
    }
    //#endif

    private static IEnumerable<Type> ValidatorContracts() =>
        typeof(ApplicationModule).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, BaseType.IsGenericType: true }
                && type.BaseType.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            .Select(type => typeof(IValidator<>).MakeGenericType(type.BaseType!.GetGenericArguments()));

    private static ServiceCollection ComposeEveryFeature()
    {
        var services = new ServiceCollection();

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        //#if (files)
        services.AddFiles(FilesConfiguration());
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif

        return services;
    }
    //#if (files)

    private static IConfiguration FilesConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Files:Categories:document:AllowedMediaTypes:0"] = "application/pdf",
                ["Files:Categories:document:MaxSizeInBytes"] = "1048576",
                ["Files:Categories:document:UploadModes:0"] = "Direct",
            })
            .Build();
    //#endif
}
