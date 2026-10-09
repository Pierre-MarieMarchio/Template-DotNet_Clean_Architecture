//#if (files)
using AppTemplate.Api.Features.Files.Jobs;
//#endif
using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.Features.Reminders.Jobs;
using AppTemplate.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

/// <summary>
/// Names the jobs' own spans and counters to the telemetry pipeline the API already registers.
/// <para>
/// Added to that pipeline rather than registered as a second one: the callbacks run only when
/// telemetry is enabled and a provider is built, so a deployment with no collector still
/// registers nothing. An instrument is recorded whether or not anything listens, and exported only
/// when named here, which <c>ObservabilityRegistrationTests</c> holds for every job.
/// </para>
/// </summary>
internal static class BackgroundJobsTelemetry
{
    internal static IServiceCollection AddBackgroundJobsTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Each job's own span per task. One AddSource per ActivitySource a job declares.
        services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing
            //#if (files)
            .AddSource(FileInstruments.Name)
            //#endif
            .AddSource(MaintenanceInstruments.Name)
            .AddSource(ReminderInstruments.Name));

        // The iteration and volume counters, plus the reminder diagnostics' missed-cancellation
        // counter, which only a firing pass can record. A meter declared and not named here is
        // measured and thrown away.
        services.ConfigureOpenTelemetryMeterProvider((_, metrics) => metrics
            //#if (files)
            .AddMeter(FileInstruments.Name)
            //#endif
            .AddMeter(MaintenanceInstruments.Name)
            .AddMeter(ReminderInstruments.Name)
            .AddMeter(PersistenceModule.RemindersMeterName));

        return services;
    }
}
