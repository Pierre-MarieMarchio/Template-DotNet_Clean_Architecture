# Observability

How to see what the API is doing: its logs, its metrics and traces, and its health probes.

## Logs

The API logs structured JSON to the console (`AddJsonConsole`, scopes included); the level of each
category is `Logging:LogLevel:*`. Every request but the health probes writes one line:

```
POST /api/v1/notes responded 201 in 12.4ms (traceIdentifier 0HN…:00000001, trace 4bf92f…).
```

The line is a fixed list of fields. It never reads headers, cookies, the query string or the
body, so a credential cannot reach the log through it; logging you add yourself carries no such
guarantee. A refusal's problem document carries the same `traceId` as `traceIdentifier`, so a
client's report joins the log line, and the line joins the trace.

## Metrics

With `OpenTelemetry:Enabled`, the API exports over OTLP the meters of ASP.NET Core, `HttpClient`,
<!--#if (postgreSql)-->
the .NET runtime, the Npgsql connection pool and the rate limiter.
<!--#endif-->
<!--#if (!postgreSql)-->
the .NET runtime and the rate limiter.
<!--#endif-->

<!--#if (files && reminders)-->
On an instance that runs jobs, the loops add `AppTemplate.Jobs.Files`, `AppTemplate.Jobs.Maintenance`
and `AppTemplate.Jobs.Reminders`, and the reminder store adds `AppTemplate.Reminders`.
<!--#endif-->
<!--#if (files && !reminders)-->
On an instance that runs jobs, the loops add `AppTemplate.Jobs.Files` and
`AppTemplate.Jobs.Maintenance`.
<!--#endif-->
<!--#if (!files && reminders)-->
On an instance that runs jobs, the loops add `AppTemplate.Jobs.Maintenance` and
`AppTemplate.Jobs.Reminders`, and the reminder store adds `AppTemplate.Reminders`.
<!--#endif-->
<!--#if (!files && !reminders)-->
On an instance that runs jobs, the maintenance loop adds `AppTemplate.Jobs.Maintenance`.
<!--#endif-->
Each pass adds one to an
`apptemplate.jobs.<loop>.iterations` counter tagged with its `task` and its `outcome`
(`success`, `failure`, `exception` or `disabled`), whether or not it found work. Alert on a flat
series where `outcome != "disabled"`: that is a loop that stopped.

## Traces

<!--#if (postgreSql)-->
Traces cover incoming requests (health probes excluded), outgoing `HttpClient` calls and Npgsql
commands.
<!--#endif-->
<!--#if (sqlServer)-->
Traces cover incoming requests (health probes excluded), outgoing `HttpClient` calls and SqlClient
commands.
<!--#endif-->
<!--#if (sqlite)-->
Traces cover incoming requests (health probes excluded) and outgoing `HttpClient` calls; SQLite
commands are not traced.
<!--#endif-->
Every trace is sampled unless `OpenTelemetry:TracesSamplingRatio` says otherwise. The
service name and version are read from the `AppTemplate.Api` assembly; `OpenTelemetry:ServiceName`
overrides the name when several deployments share a collector.

An unreachable collector is harmless: start-up succeeds, and failed exports are reported on the
exporter's own event source, not in the log. With `Enabled` false, nothing is registered.

## Health checks

| Route | Checks | Use it for |
|---|---|---|
| `/health` | nothing: the process answers | liveness, the Compose and Dockerfile healthchecks |
| `/health/ready` | the database, and whether shutdown has started | readiness, routing traffic |

Both are anonymous and outside the rate limiter. Readiness turns unhealthy the moment shutdown
starts, so an orchestrator stops sending traffic while `Shutdown:Timeout` drains what arrived.
Liveness touches no dependency, so a brief database outage never restarts a healthy process.
Neither says whether the jobs run: the `iterations` counters do.

## Configure

| Section | What it sets |
|---|---|
| [`OpenTelemetry`](../reference/configuration.md#opentelemetry) | the switch, the OTLP endpoint and protocol, the service name, the sampling ratio |
| [`Shutdown`](../reference/configuration.md#shutdown) | how long in-flight requests drain |
| [Standard ASP.NET Core keys](../reference/configuration.md#standard-aspnet-core-keys) | `Logging:LogLevel:*` |

## Decisions

- [0001 Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): OpenTelemetry over OTLP.
