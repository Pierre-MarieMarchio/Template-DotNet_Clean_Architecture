//#if (localAuth)
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
//#endif
using AppTemplate.Application.Core.Common.Ports;
//#if (files)
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
//#endif
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
//#if (localAuth)
using AppTemplate.Infrastructure.InMemory.Features.ExternalLogins;
//#endif
//#if (files)
using AppTemplate.Infrastructure.InMemory.Features.Files;
//#endif
//#if (reminders)
using AppTemplate.Infrastructure.InMemory.Features.Reminders;
//#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Infrastructure.InMemory;

public static class InMemoryModule
{
    public static IServiceCollection AddInMemoryModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IDateTimeProvider>();
        services.RemoveAll<FixedDateTimeProvider>();
        services.AddSingleton<FixedDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider>(
            serviceProvider => serviceProvider.GetRequiredService<FixedDateTimeProvider>());

        services.RemoveAll<IEmailSender>();
        services.RemoveAll<RecordedEmails>();
        services.AddSingleton<RecordedEmails>();
        services.AddScoped<IEmailSender, InMemoryEmailSender>();

        //#if (reminders)
        services.AddInMemoryReminderNotifications();
        //#endif
        //#if (files)
        services.AddInMemoryFileContent();
        //#endif
        //#if (localAuth)
        services.AddInMemoryExternalIdentities();
        //#endif

        return services;
    }
    //#if (localAuth)

    public static IServiceCollection AddInMemoryExternalIdentities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IExternalIdentityVerifier>();
        services.RemoveAll<AcceptedExternalIdentities>();
        services.AddSingleton<AcceptedExternalIdentities>();
        services.AddScoped<IExternalIdentityVerifier, InMemoryExternalIdentityVerifier>();

        return services;
    }
    //#endif
    //#if (reminders)

    public static IServiceCollection AddInMemoryReminderNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IReminderNotifier>();
        services.RemoveAll<RecordedReminderNotifications>();
        services.AddSingleton<RecordedReminderNotifications>();
        services.AddScoped<IReminderNotifier, InMemoryReminderNotifier>();

        return services;
    }
    //#endif
    //#if (files)

    public static IServiceCollection AddInMemoryOwnerQuotaLock(
        this IServiceCollection services,
        TimeSpan? waitLimit = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IOwnerQuotaLock>();
        services.AddSingleton<IOwnerQuotaLock>(
            new InMemoryOwnerQuotaLock(waitLimit ?? InMemoryOwnerQuotaLock.DefaultWaitLimit));

        return services;
    }

    public static IServiceCollection AddInMemoryOrphanSweepCheckpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IOrphanSweepCheckpoint>();
        services.AddSingleton<IOrphanSweepCheckpoint, InMemoryOrphanSweepCheckpoint>();

        return services;
    }

    public static IServiceCollection AddInMemoryFileContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IFileContentStore>();
        services.RemoveAll<IFileDirectTransfer>();
        services.RemoveAll<IFileContentInventory>();
        services.RemoveAll<IFileContentScanner>();
        services.RemoveAll<StoredObjects>();
        services.RemoveAll<ArrangedScans>();
        services.AddSingleton<StoredObjects>();
        services.AddSingleton<ArrangedScans>();
        services.AddScoped<IFileContentStore, InMemoryFileContentStore>();
        services.AddScoped<IFileDirectTransfer, InMemoryFileDirectTransfer>();
        services.AddScoped<IFileContentInventory, InMemoryFileContentInventory>();
        services.AddScoped<IFileContentScanner, InMemoryFileContentScanner>();

        return services;
    }
    //#endif
}
