//#if (canAddFeature)
using AppTemplate.Application.Core;
//#endif
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
//#endif
//#if (externalAuth)
using AppTemplate.Application.Features.ExternalIdentity.Services;
//#endif
//#if (files)
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.Services.Consumers.AccountDeleted;
using AppTemplate.Application.Features.Files.Services.Consumers.StoredFileDeleted;
//#endif
//#if (notes)
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.Application.Features.Notes.Services.Consumers;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Services;
using AppTemplate.Application.Features.Reminders.Services.Consumers.AccountDeleted;
using AppTemplate.Application.Features.Reminders.Services.Consumers.TodoItemCompleted;
//#endif
//#if (todoLists)
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.Features.TodoLists.Services.Consumers.AccountDeleted;
using AppTemplate.Application.Features.TodoLists.Services.Consumers.TodoItemCompleted;
//#endif
//#if (files)
using AppTemplate.Domain.Features.Files.Model.Events;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Model.Events;
//#endif
//#if (canAddFeature)
using FluentValidation;
//#endif
//#if (files)
using Microsoft.Extensions.Configuration;
//#endif
//#if (canAddFeature)
using Microsoft.Extensions.DependencyInjection;
//#endif
//#if (files)
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
//#endif

namespace AppTemplate.Application;

public static class ApplicationModule
{
    //#if (todoLists)
    private const string _todoLists = "TodoLists";
    //#endif
    //#if (reminders)
    private const string _reminders = "Reminders";
    //#endif
    //#if (files)
    private const string _files = "Files";
    //#endif
    //#if (notes)
    private const string _notes = "Notes";
    //#endif
    //#if (todoLists)

    public static IServiceCollection AddTodoLists(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_todoLists);
        services.AddScoped<ITodoListService, TodoListService>();

        services.AddDomainEventConsumer<TodoItemCompletedDomainEvent, LogTodoItemCompletedConsumer>();

        services.AddDomainEventConsumer<UserAccountDeleted, DeleteTodoListsOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteTodoListsOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (reminders)

    public static IServiceCollection AddReminders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_reminders);
        services.AddScoped<IReminderService, ReminderService>();

        services.AddDomainEventConsumer<
            TodoItemCompletedDomainEvent, CancelRemindersOnTodoItemCompletedConsumer>();

        services.AddDomainEventConsumer<UserAccountDeleted, DeleteRemindersOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteRemindersOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (files)

    public static IServiceCollection AddFiles(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FilesOptions>()
            .Bind(configuration.GetSection(FilesOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FilesOptions>, FilesOptionsValidator>());

        services.AddFeature(_files);
        services.AddScoped<IStoredFileService, StoredFileService>();
        services.AddScoped<IStoredFileRegistrationService, StoredFileRegistrationService>();
        services.AddScoped<IStoredFileDepositService, StoredFileDepositService>();

        services.AddDomainEventConsumer<
            StoredFileDeletedDomainEvent, ReclaimContentOnStoredFileDeletedConsumer>();

        services.AddDomainEventConsumer<UserAccountDeleted, DeleteStoredFilesOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteStoredFilesOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (notes)

    public static IServiceCollection AddNotes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_notes);
        services.AddScoped<INoteService, NoteService>();

        services.AddDomainEventConsumer<UserAccountDeleted, DeleteNotesOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteNotesOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (externalAuth)

    public static IServiceCollection AddExternalIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IExternalIdentityService, ExternalIdentityService>();

        return services;
    }
    //#endif
    //#if (canAddFeature)

    private static void AddFeature(this IServiceCollection services, string vertical)
    {
        services.AddValidatorsFromAssembly(
            typeof(ApplicationModule).Assembly,
            lifetime: ServiceLifetime.Scoped,
            filter: scan => IsInVertical(scan.ValidatorType, vertical),
            includeInternalTypes: true);

        services.AddUseCases(TypesIn(vertical));
    }

    private static IEnumerable<Type> TypesIn(string vertical) =>
        typeof(ApplicationModule).Assembly
            .GetTypes()
            .Where(type => IsInVertical(type, vertical));

    private static bool IsInVertical(Type type, string vertical) =>
        type.Namespace?.StartsWith(
            $"{typeof(ApplicationModule).Namespace}.Features.{vertical}.",
            StringComparison.Ordinal) == true;
    //#endif
}
