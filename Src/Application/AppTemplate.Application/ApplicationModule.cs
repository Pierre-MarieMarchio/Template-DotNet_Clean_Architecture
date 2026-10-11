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
using AppTemplate.Application.Features.Files.Consumers.AccountDeleted;
using AppTemplate.Application.Features.Files.Consumers.StoredFileDeleted;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Services;
//#endif
//#if (notes)
using AppTemplate.Application.Features.Notes.Consumers.AccountDeleted;
using AppTemplate.Application.Features.Notes.Services;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Consumers.AccountDeleted;
using AppTemplate.Application.Features.Reminders.Consumers.TodoItemCompleted;
using AppTemplate.Application.Features.Reminders.Services;
//#endif
//#if (todoLists)
using AppTemplate.Application.Features.TodoLists.Consumers.AccountDeleted;
using AppTemplate.Application.Features.TodoLists.Consumers.TodoItemCompleted;
using AppTemplate.Application.Features.TodoLists.Services;
//#endif
//#if (files)
using AppTemplate.Domain.Features.Files.Events;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Events;
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

/// <summary>
/// One call per feature, and a host composes the features it offers. There is no call that adds all
/// of them: deleting a feature's folder and its one line here is the whole removal, and a single
/// entry point scanning the assembly would put every port every feature declares into every host's
/// graph, which under <c>ValidateOnBuild</c> makes each of them mandatory everywhere. A feature
/// takes an <c>IConfiguration</c> only when it has application policy a deployment declares, as the
/// Files categories are.
/// </summary>
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

    /// <summary>
    /// Registers the to-do list feature: its use cases, its validators, and the access service its
    /// use cases share.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTodoLists(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_todoLists);
        services.AddScoped<ITodoListService, TodoListService>();

        services.AddDomainEventConsumer<TodoItemCompletedDomainEvent, LogTodoItemCompletedConsumer>();

        // The declared fate of this feature's data once the account that owned it is gone, applied
        // by the consumer when the fact arrives and by the sweep when it never does.
        services.AddDomainEventConsumer<UserAccountDeleted, DeleteTodoListsOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteTodoListsOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (reminders)

    /// <summary>
    /// Registers the reminder feature. Not independent of <see cref="AddTodoLists"/>: scheduling a
    /// reminder reaches into the to-do list's read port, which is the only ownership check made
    /// before a reminder is created, so a host that composes this without that one resolves
    /// nothing.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddReminders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_reminders);
        services.AddScoped<IReminderService, ReminderService>();

        // A second consumer of an event the to-do list feature raises: both run when an item is
        // completed, neither aware of the other.
        services.AddDomainEventConsumer<
            TodoItemCompletedDomainEvent, CancelRemindersOnTodoItemCompletedConsumer>();

        // The declared fate of this feature's data once the account that owned it is gone, applied
        // by the consumer when the fact arrives and by the sweep when it never does.
        services.AddDomainEventConsumer<UserAccountDeleted, DeleteRemindersOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteRemindersOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (files)

    /// <summary>
    /// Registers the stored-file feature and binds <c>Files</c> (its categories and the API upload
    /// ceiling), validated at start-up.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Supplies the <c>Files</c> section.</param>
    /// <returns>The same collection, for chaining.</returns>
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

        // The prompt half of reclaiming a deleted file's bytes. The orphan sweep is what makes it
        // correct; this only makes it fast — see the consumer's own doc.
        services.AddDomainEventConsumer<
            StoredFileDeletedDomainEvent, ReclaimContentOnStoredFileDeletedConsumer>();

        // The declared fate of this feature's data once the account that owned it is gone, applied
        // by the consumer when the fact arrives and by the sweep when it never does.
        services.AddDomainEventConsumer<UserAccountDeleted, DeleteStoredFilesOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteStoredFilesOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (notes)

    /// <summary>
    /// Registers the notes feature: its service and its validator. No use cases, and the one event
    /// it answers is the account-deleted fact; depends on none of the other features.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNotes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_notes);
        services.AddScoped<INoteService, NoteService>();

        // The declared fate of this feature's data once the account that owned it is gone, applied
        // by the consumer when the fact arrives and by the sweep when it never does.
        services.AddDomainEventConsumer<UserAccountDeleted, DeleteNotesOnAccountDeletedConsumer>();
        services.AddScoped<IDeletedAccountDataPolicy, DeleteNotesOnAccountDeletedConsumer>();

        return services;
    }
    //#endif
    //#if (externalAuth)

    /// <summary>
    /// Registers the mapping from an external identity provider's subject to an internal owner. Not
    /// an example: a host authenticating callers with an external provider composes it through its
    /// external-authentication entry point, and a host with local accounts leaves it out.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddExternalIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IExternalIdentityService, ExternalIdentityService>();

        return services;
    }
    //#endif
    //#if (canAddFeature)

    /// <summary>
    /// The use cases and validators one vertical declares, discovered by the folder they sit in.
    /// Discovery within a feature, opt-in between features: a hand-written list of use cases could
    /// only go stale, and the namespace already states which feature a type belongs to.
    /// </summary>
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
