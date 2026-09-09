using AppTemplate.Presentation.Core.Common.Observability;
using AppTemplate.Worker.Features.Files;
using AppTemplate.Worker.Features.Maintenance;
using AppTemplate.Worker.Features.Reminders;
using Npgsql;

namespace AppTemplate.Worker.Common.Observability;

/// <summary>
/// What only this host can say about its own telemetry, on top of
/// <see cref="AppTemplate.Presentation.Core.Common.Observability.ObservabilityExtensions"/>: the
/// three loops' own spans and counters, and the database instrumentation.
/// <para>
/// No ASP.NET Core instrumentation, because this host answers no request. Outbound HTTP <em>is</em>
/// instrumented, and that comes from the shared registration: the modules this host composes are
/// the ones that call outwards, and a call it makes without a span is a call nobody can see failed.
/// </para>
/// <para>
/// The names below cannot move down with the rest. Each is a constant on a class in this host's own
/// feature folders, so a shared project naming them would have to reference the host that
/// references it.
/// </para>
/// </summary>
public static class WorkerObservabilityExtensions
{
    public static IServiceCollection AddWorkerObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddObservability(
            configuration,
            typeof(WorkerObservabilityExtensions).Assembly,
            // Each loop's own span per task, plus the Npgsql span for the statements it issues. One
            // AddSource per host-owned ActivitySource, and ObservabilityRegistrationTests fails the
            // build for a missing one.
            tracing => tracing
                .AddSource(FileInstruments.Name)
                .AddSource(MaintenanceInstruments.Name)
                .AddSource(ReminderInstruments.Name)
                .AddNpgsql(),
            // The three loops' iteration and volume counters. A meter this host declares but does
            // not name here is measured and thrown away, so the same test guards these.
            metrics => metrics
                .AddMeter(FileInstruments.Name)
                .AddMeter(MaintenanceInstruments.Name)
                .AddMeter(ReminderInstruments.Name)
                // ReminderDiagnostics's missed-cancellation counter. A literal because that class is
                // internal to another project, as "Npgsql" below is a meter this host does not own.
                .AddMeter("AppTemplate.Reminders")
                .AddMeter("Npgsql"));
    }
}
