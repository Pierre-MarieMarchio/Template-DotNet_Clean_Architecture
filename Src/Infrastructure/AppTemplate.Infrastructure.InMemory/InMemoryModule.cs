using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Infrastructure.InMemory.Email;
using AppTemplate.Infrastructure.InMemory.Reminders;
using AppTemplate.Infrastructure.InMemory.Time;
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
    /// Replaces the clock, the email sender and the reminder notifier with controllable, recording
    /// doubles. <see cref="IReminderNotifier"/>'s real adapter (<c>EmailReminderNotifier</c>, in
    /// <c>AppTemplate.Infrastructure.Email</c>) sends actual mail, exactly like
    /// <see cref="IEmailSender"/>'s, so it gets the same treatment here.
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
}
