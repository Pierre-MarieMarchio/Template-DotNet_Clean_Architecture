using System.Reflection;
using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests;

public sealed class ApplicationModuleTests
{
    /// <summary>
    /// Fifteen to-do list operations, five reminder ones, nine for files — of which three, the
    /// abandonment purge, the orphan reclamation and the deposit inspection, are reached only from
    /// background jobs.
    /// </summary>
    private const int _knownUseCaseCount = 32;

    public static TheoryData<Type> UseCaseImplementations =>
        [.. UseCaseDiscovery.Implementations];

    /// <summary>The four calls a host composes, one per vertical.</summary>
    public static TheoryData<string> FeatureCalls =>
    [
        nameof(ApplicationModule.AddTodoLists),
        nameof(ApplicationModule.AddReminders),
        nameof(ApplicationModule.AddFiles),
        nameof(ApplicationModule.AddNotes),
    ];

    [Fact]
    public void TheDiscovery_FindsEveryUseCaseTheLayerDeclares() =>
        UseCaseDiscovery.Implementations.Count.ShouldBe(
            _knownUseCaseCount,
            "A use case was added or removed without this count following it. Discovery is what puts " +
            "it in the container, so the count is the only place the number is stated at all.");

    /// <summary>
    /// Each use case resolves through its own interface and nothing else: a container that binds the
    /// concrete class would let a controller depend on the implementation.
    /// </summary>
    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void EveryUseCase_ResolvesThroughItsOwnInterface(Type implementation)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService(UseCaseDiscovery.ContractOf(implementation))
            .ShouldBeOfType(implementation);
    }

    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void EveryUseCase_IsScopedAndBoundExactlyOnce(Type implementation)
    {
        var services = ComposeEveryFeature();

        services.Single(descriptor => descriptor.ServiceType == UseCaseDiscovery.ContractOf(implementation))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Theory]
    [MemberData(nameof(UseCaseImplementations))]
    public void NoUseCase_IsBoundToItsConcreteType(Type implementation)
    {
        var services = ComposeEveryFeature();

        services.ShouldNotContain(descriptor => descriptor.ServiceType == implementation);
    }

    /// <summary>
    /// The validators are discovered, not listed, so a command whose validator was never
    /// written would fail to resolve here rather than silently skip validation.
    /// </summary>
    [Theory]
    [InlineData(typeof(IValidator<CreateTodoListCommand>))]
    [InlineData(typeof(IValidator<RenameTodoListCommand>))]
    [InlineData(typeof(IValidator<AddTodoItemCommand>))]
    [InlineData(typeof(IValidator<DeleteTodoListCommand>))]
    [InlineData(typeof(IValidator<CompleteTodoItemCommand>))]
    [InlineData(typeof(IValidator<RemoveTodoItemCommand>))]
    [InlineData(typeof(IValidator<UpdateTodoItemCommand>))]
    [InlineData(typeof(IValidator<ReopenTodoItemCommand>))]
    [InlineData(typeof(IValidator<AddTagToTodoItemCommand>))]
    [InlineData(typeof(IValidator<RemoveTagFromTodoItemCommand>))]
    [InlineData(typeof(IValidator<ReplaceTodoItemTagsCommand>))]
    [InlineData(typeof(IValidator<GetTodoListQuery>))]
    [InlineData(typeof(IValidator<GetTodoItemQuery>))]
    [InlineData(typeof(IValidator<GetTodoItemsQuery>))]
    [InlineData(typeof(IValidator<ScheduleReminderCommand>))]
    [InlineData(typeof(IValidator<RegisterFileCommand>))]
    [InlineData(typeof(IValidator<NoteContentDto>))]
    public void EveryValidator_IsDiscovered(Type validatorType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService(validatorType).ShouldNotBeNull();
    }

    /// <summary>
    /// Each <c>IValidator&lt;T&gt;</c> is bound exactly once. A second registration mechanism would
    /// leave "last one wins" deciding which instance a use case received.
    /// </summary>
    [Theory]
    [InlineData(typeof(IValidator<CreateTodoListCommand>))]
    [InlineData(typeof(IValidator<ScheduleReminderCommand>))]
    [InlineData(typeof(IValidator<RegisterFileCommand>))]
    [InlineData(typeof(IValidator<NoteContentDto>))]
    public void EachValidator_IsRegisteredExactlyOnce(Type validatorType)
    {
        var services = ComposeEveryFeature();

        services.Count(descriptor => descriptor.ServiceType == validatorType).ShouldBe(1);
    }

    /// <summary>
    /// Not a use case, so the marker-based discovery never reaches it: it has to be bound by hand,
    /// and this is the one place that checks the hand-written line was not forgotten.
    /// </summary>
    [Fact]
    public void TodoListService_IsRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddTodoLists();

        services.Single(descriptor => descriptor.ServiceType == typeof(ITodoListService))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// The notes feature has no use case for the marker-based discovery to find, so its service is
    /// bound by hand like the other services, and this is where that line is checked.
    /// </summary>
    [Fact]
    public void NoteService_IsRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddNotes();

        services.Single(descriptor => descriptor.ServiceType == typeof(INoteService))
            .Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// One call registers one vertical and leaves the others out of the container, which is what
    /// lets a host offer some of the features and not all of them.
    /// </summary>
    [Fact]
    public void AFeatureCall_RegistersItsOwnVerticalAndNoOther()
    {
        var services = new ServiceCollection();
        services.AddTodoLists();

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(ICreateTodoListUseCase));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IScheduleReminderUseCase));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IRegisterFileUseCase));
    }

    [Theory]
    [MemberData(nameof(FeatureCalls))]
    public void EveryFeatureCall_Rejects_ANullServiceCollection(string call) =>
        Should.Throw<ArgumentNullException>(() => Invoke(call, null!));

    /// <summary>
    /// Only the Files feature reads settings — its categories are application policy a deployment
    /// declares — so it alone takes an <c>IConfiguration</c>, and no other feature call may.
    /// </summary>
    [Theory]
    [MemberData(nameof(FeatureCalls))]
    public void OnlyTheFilesCall_AsksForConfiguration(string call) =>
        MethodNamed(call).GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            call == nameof(ApplicationModule.AddFiles)
                ? [typeof(IServiceCollection), typeof(IConfiguration)]
                : [typeof(IServiceCollection)]);

    [Fact]
    public void TheFilesCall_Rejects_ANullConfiguration() =>
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddFiles(null!));

    /// <summary>
    /// The Files section is validated when the host starts, so a deployment with no category, or a
    /// malformed one, does not boot.
    /// </summary>
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

    private static MethodInfo MethodNamed(string call) =>
        typeof(ApplicationModule).GetMethod(call)!;

    private static void Invoke(string call, IServiceCollection services)
    {
        object?[] arguments = call == nameof(ApplicationModule.AddFiles)
            ? [services, FilesConfiguration()]
            : [services];

        try
        {
            MethodNamed(call).Invoke(null, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>
    /// The four verticals together, which is what the assembly-wide assertions need: they speak of
    /// every use case the layer declares, so every feature declaring one has to be composed.
    /// </summary>
    private static ServiceCollection ComposeEveryFeature()
    {
        var services = new ServiceCollection();

        services.AddTodoLists();
        services.AddReminders();
        services.AddFiles(FilesConfiguration());
        services.AddNotes();

        return services;
    }

    /// <summary>The smallest valid <c>Files</c> section: one category.</summary>
    private static IConfiguration FilesConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Files:Categories:document:AllowedMediaTypes:0"] = "application/pdf",
                ["Files:Categories:document:MaxSizeInBytes"] = "1048576",
                ["Files:Categories:document:UploadModes:0"] = "Direct",
            })
            .Build();

    /// <summary>
    /// Scope validation plus eager building means a missing dependency fails here rather than at
    /// the first request.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = ComposeEveryFeature();

        services.AddScoped(_ => Substitute.For<ITodoListRepository>());
        services.AddScoped(_ => Substitute.For<ITodoListQueries>());
        services.AddScoped(_ => Substitute.For<ITodoItemQueries>());
        services.AddScoped(_ => Substitute.For<ITodoItemTagQueries>());
        services.AddScoped(_ => Substitute.For<IReminderRepository>());
        services.AddScoped(_ => Substitute.For<IReminderNotifier>());
        services.AddScoped(_ => Substitute.For<IReminderRecipientDirectory>());
        services.AddScoped(_ => Substitute.For<IReminderQueries>());
        services.AddScoped(_ => Substitute.For<IReminderDiagnostics>());
        services.AddScoped(_ => Substitute.For<INoteQueries>());
        services.AddScoped(_ => Substitute.For<INoteStore>());
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        services.AddScoped(_ => Substitute.For<ILeaderLease>());
        services.AddScoped(_ => Substitute.For<IStoredFileRepository>());
        services.AddScoped(_ => Substitute.For<IStoredFileQueries>());
        services.AddScoped(_ => Substitute.For<IStoredFileTagQueries>());
        services.AddScoped(_ => Substitute.For<IFileContentStore>());
        services.AddScoped(_ => Substitute.For<IFileContentInventory>());
        services.AddScoped(_ => Substitute.For<IFileContentScanner>());
        services.AddScoped(_ => Substitute.For<IFileDirectTransfer>());
        services.AddSingleton(_ => Substitute.For<IOwnerQuotaLock>());
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddScoped(_ => Substitute.For<IDateTimeProvider>());
        services.AddScoped(_ => Substitute.For<IIdempotencyStore>());
        services.AddScoped(_ => Substitute.For<ICacheStore>());

        // The layer's domain-event consumers take an ILogger, which every real host supplies.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
