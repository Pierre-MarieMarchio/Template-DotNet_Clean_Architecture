//#if (files)
using AppTemplate.Api.Features.Files.Jobs;
//#endif
using AppTemplate.Api.Features.Maintenance.Jobs;
//#if (reminders)
using AppTemplate.Api.Features.Reminders.Jobs;
using AppTemplate.Infrastructure.Persistence;
//#endif
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

internal static class BackgroundJobsTelemetry
{
    internal static IServiceCollection AddBackgroundJobsTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing
            //#if (files)
            .AddSource(FileInstruments.Name)
            //#endif
            //#if (reminders)
            .AddSource(ReminderInstruments.Name)
            //#endif
            .AddSource(MaintenanceInstruments.Name));

        services.ConfigureOpenTelemetryMeterProvider((_, metrics) => metrics
            //#if (files)
            .AddMeter(FileInstruments.Name)
            //#endif
            //#if (reminders)
            .AddMeter(ReminderInstruments.Name)

            .AddMeter(PersistenceModule.RemindersMeterName)
            //#endif
            .AddMeter(MaintenanceInstruments.Name));

        return services;
    }
}
