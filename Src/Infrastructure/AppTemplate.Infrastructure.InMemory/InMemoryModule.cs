using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
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
    /// <see cref="IEmailSender"/>'s, so it gets the same treatment here; the two file ports' real
    /// adapters talk to an S3 bucket and <see cref="IExternalIdentityVerifier"/>'s fetches a key set
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
    /// The real adapter (<c>ExternalIdentityVerifier</c>, in <c>AppTemplate.Infrastructure.Identity</c>)
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
    /// Registers the in-memory object store behind all three file ports, without the clock and
    /// email-sender swaps <see cref="AddInMemoryModule"/> also makes.
    /// <para>
    /// The three ports share one <see cref="StoredObjects"/>, and they have to: the sweep behind
    /// <see cref="IFileContentInventory"/> lists what <see cref="IFileContentStore"/> holds and
    /// deletes through it, so two instances would make the inventory answer about a bucket nothing
    /// ever writes to and the sweep would reclaim nothing while appearing to work. The inspector
    /// reads the same objects for the same reason: it answers about the bytes a deposit left, and a
    /// second store would mean it answered about none.
    /// </para>
    /// <para>
    /// It needs a clock — the double stamps a deposit and expires a grant from
    /// <see cref="IDateTimeProvider"/> — which <see cref="AddInMemoryModule"/> registers just above.
    /// Called on its own, it inherits whatever clock the host already has.
    /// </para>
    /// </summary>
    public static IServiceCollection AddInMemoryFileContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IFileContentStore>();
        services.RemoveAll<IFileContentInventory>();
        services.RemoveAll<IFileContentInspector>();
        services.RemoveAll<StoredObjects>();
        services.RemoveAll<ArrangedInspections>();
        services.AddSingleton<StoredObjects>();
        services.AddSingleton<ArrangedInspections>();
        services.AddScoped<IFileContentStore, InMemoryFileContentStore>();
        services.AddScoped<IFileContentInventory, InMemoryFileContentInventory>();
        services.AddScoped<IFileContentInspector, InMemoryFileContentInspector>();

        return services;
    }
}
