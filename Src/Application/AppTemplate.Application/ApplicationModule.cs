//#if (anyFeature)
using AppTemplate.Application.Core;
//#endif
//#if (files)
using AppTemplate.Application.Features.Files.Consumers.StoredFileDeleted;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Services;
//#endif
//#if (notes)
using AppTemplate.Application.Features.Notes.Services;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Consumers.TodoItemCompleted;
using AppTemplate.Application.Features.Reminders.Services;
//#endif
//#if (todoLists)
using AppTemplate.Application.Features.TodoLists.Consumers.TodoItemCompleted;
using AppTemplate.Application.Features.TodoLists.Services;
//#endif
//#if (files)
using AppTemplate.Domain.Features.Files.Events;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Events;
//#endif
//#if (anyFeature)
using FluentValidation;
//#endif
//#if (files)
using Microsoft.Extensions.Configuration;
//#endif
//#if (anyFeature)
using Microsoft.Extensions.DependencyInjection;
//#endif
//#if (files)
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
//#endif

namespace AppTemplate.Application;

/// <summary>
/// One call per feature, and a host composes the features it offers.
/// <para>
/// There is no call that adds all of them. That is what makes a feature removable: deleting its
/// folder and its one line here is the whole operation, and nothing else claims to have registered
/// it. A single entry point scanning the assembly would put every port every feature declares into
/// every host's graph, which under <c>ValidateOnBuild</c> makes each of them mandatory everywhere.
/// </para>
/// <para>
/// A feature takes an <c>IConfiguration</c> only when it has application policy a deployment
/// declares, as the Files categories are. The other features read no settings.
/// </para>
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

        return services;
    }
    //#endif
    //#if (reminders)

    /// <summary>
    /// Registers the reminder feature.
    /// <para>
    /// <b>Not independent of <see cref="AddTodoLists"/>.</b> Scheduling a reminder reaches into the
    /// to-do list's read port, which is the only ownership check made before a reminder is created,
    /// so a host that composes this without that one resolves nothing. The two example features are
    /// not symmetric, and saying so here is cheaper than a start-up failure that does not explain
    /// itself.
    /// </para>
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

        return services;
    }
    //#endif
    //#if (notes)

    /// <summary>
    /// Registers the notes feature: its service and its validator. It has no use cases and answers no
    /// domain event, and it depends on none of the other features.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNotes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFeature(_notes);
        services.AddScoped<INoteService, NoteService>();

        return services;
    }
    //#endif
    //#if (anyFeature)

    /// <summary>
    /// The use cases and validators one vertical declares, discovered by the folder they sit in.
    /// <para>
    /// Discovery within a feature, opt-in between features. A hand-written list of thirty use cases
    /// per feature could only go stale, and the namespace already states which feature a type
    /// belongs to — a rule elsewhere holds that it does.
    /// </para>
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
