using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.InMemory.Features.Auth;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using AppTemplate.Infrastructure.InMemory.Features.Reminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Infrastructure.InMemory;

/// <summary>
/// Substitutes the adapters that talk to the outside world with doubles that do not.
/// <para>
/// This module has one reason to change: what a test host needs in place of a real
/// dependency. It is a product project rather than a file in a test assembly so that every
/// host composes it the same way — <c>AddInMemoryModule</c> after the real modules — instead
/// of each test project growing its own fake and its own registration order.
/// </para>
/// </summary>
public static class InMemoryModule
{
    /// <summary>
    /// Replaces the clock, the email sender, the reminder notifier, the object store and the
    /// external identity verifier with controllable, recording doubles.
    /// <see cref="IReminderNotifier"/>'s real adapter (<c>EmailReminderNotifier</c>, in
    /// <c>AppTemplate.Infrastructure.Email</c>) sends actual mail, exactly like
    /// <see cref="IEmailSender"/>'s, so it gets the same treatment here; the file ports' real
    /// adapters talk to a bucket, a disk and a scanner, and <see cref="IExternalIdentityVerifier"/>'s fetches a key set
    /// from an identity provider, which are the same kind of dependency.
    /// <para>
    /// It <b>removes and re-adds</b> rather than relying on last-registration-wins, and it
    /// takes no <c>IConfiguration</c> because there is nothing to configure. Replacement is
    /// explicit: a double that only worked when it happened to be registered last is a
    /// silent dependency on composition order, and the failure mode is a test that quietly
    /// exercised the real SMTP client.
    /// </para>
    /// <para>
    /// Call it <em>after</em> the modules whose adapters it replaces. Calling it before them
    /// is a no-op that leaves the real adapters in place, which is why the call order in the
    /// host matters and is documented there.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInMemoryModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One instance, resolvable both as the port and as the concrete type: production code
        // gets IDateTimeProvider, the test moves time through FixedDateTimeProvider, and both
        // are looking at the same clock.
        services.RemoveAll<IDateTimeProvider>();
        services.RemoveAll<FixedDateTimeProvider>();
        services.AddSingleton<FixedDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider>(
            serviceProvider => serviceProvider.GetRequiredService<FixedDateTimeProvider>());

        services.RemoveAll<IEmailSender>();
        services.RemoveAll<RecordedEmails>();
        services.AddSingleton<RecordedEmails>();
        services.AddScoped<IEmailSender, InMemoryEmailSender>();

        services.AddInMemoryReminderNotifications();
        services.AddInMemoryFileContent();
        services.AddInMemoryExternalIdentities();

        return services;
    }

    /// <summary>
    /// Registers the arranged <see cref="IExternalIdentityVerifier"/> on its own, without the clock
    /// and email-sender swaps <see cref="AddInMemoryModule"/> also makes.
    /// <para>
    /// The real adapter (<c>ExternalIdentityVerifier</c>, in <c>AppTemplate.Infrastructure.Auth</c>)
    /// fetches a provider's key set over HTTP, so it is an outward call like every other one replaced
    /// here — and it is also the only port a test could not reach any other way, since presenting a
    /// token it would accept means holding Google's private key.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInMemoryExternalIdentities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IExternalIdentityVerifier>();
        services.RemoveAll<AcceptedExternalIdentities>();
        services.AddSingleton<AcceptedExternalIdentities>();
        services.AddScoped<IExternalIdentityVerifier, InMemoryExternalIdentityVerifier>();

        return services;
    }

    /// <summary>
    /// Registers the recording <see cref="IReminderNotifier"/> on its own, without the clock and
    /// email-sender swaps <see cref="AddInMemoryModule"/> also makes — useful to a test that wants
    /// reminders recorded without also freezing the clock or rerouting every other mail the host
    /// sends.
    /// </summary>
    public static IServiceCollection AddInMemoryReminderNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IReminderNotifier>();
        services.RemoveAll<RecordedReminderNotifications>();
        services.AddSingleton<RecordedReminderNotifications>();
        services.AddScoped<IReminderNotifier, InMemoryReminderNotifier>();

        return services;
    }

    /// <summary>
    /// Replaces the owner's quota lock with one held in this process, for a host that runs alone and
    /// has no PostgreSQL to take the lock from. Not part of <see cref="AddInMemoryModule"/>: a test
    /// host with a real database keeps the database lock, which is the one that holds across hosts.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="waitLimit">How long a registration waits for the lock; ten seconds when omitted,
    /// as in the PostgreSQL adapter.</param>
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

    /// <summary>
    /// Replaces the orphan sweep's checkpoint with one held in this process, for a host without
    /// PostgreSQL. Not part of <see cref="AddInMemoryModule"/>, for the same reason as
    /// <see cref="AddInMemoryOwnerQuotaLock"/>.
    /// </summary>
    public static IServiceCollection AddInMemoryOrphanSweepCheckpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IOrphanSweepCheckpoint>();
        services.AddSingleton<IOrphanSweepCheckpoint, InMemoryOrphanSweepCheckpoint>();

        return services;
    }

    /// <summary>
    /// Registers the in-memory object store behind the four file ports, without the clock and
    /// email-sender swaps <see cref="AddInMemoryModule"/> also makes.
    /// <para>
    /// The store, the direct transfer and the inventory share one <see cref="StoredObjects"/>: the
    /// sweep lists what the store holds and deletes through it, so two instances would make it
    /// reclaim nothing while appearing to work. The scanner answers from <see cref="ArrangedScans"/>.
    /// It needs a clock, which <see cref="AddInMemoryModule"/> registers; called on its own, it
    /// inherits the host's.
    /// </para>
    /// </summary>
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
}
