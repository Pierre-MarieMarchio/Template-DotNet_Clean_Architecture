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
guarantee. A use case that fails writes one more line, under the `AppTemplate.UseCases` category at
`Information`: its message's name, its error code and its duration, never the message itself. A refusal's problem document carries the same `traceId` as `traceIdentifier`, so a
client's report joins the log line, and the line joins the trace.

With `OpenTelemetry:Enabled`, the same records also leave over OTLP, to the endpoint and with the
protocol the metrics and traces use, and the console keeps writing. Each exported record carries its
level, category, trace and span identifiers, its formatted message, its scopes and the values the
message names as attributes: what the console line carries, no more. The collector receives
those values in protobuf fields, so a line break in a value stays inside its field; that is why the
rule that every host writes JSON to the console needs no counterpart here. The export applies the
same `Logging:LogLevel:*` thresholds as the console. It adds no request header, cookie or body
to a record: the request line above reads none of them, and anything you log yourself is exported as you wrote it, so keep
a credential or a personal value out of a message, a scope and a structured value, as for the
console.

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
Every use case, in every host, records its duration on the `AppTemplate.UseCases` meter, in the
`apptemplate.use_cases.duration` histogram tagged with its `use_case`, the message's name, and its
`outcome` (`success`, `failure` or `exception`).

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
Every use case adds a span named after its message, from the `AppTemplate.UseCases` source, tagged
with its `outcome` and, on a failure, its `error_code`.
Every trace is sampled unless `OpenTelemetry:TracesSamplingRatio` says otherwise. The
service name and version are read from the `AppTemplate.Api` assembly; `OpenTelemetry:ServiceName`
overrides the name when several deployments share a collector.

An unreachable collector is harmless: start-up succeeds, and failed exports are reported on the
exporter's own event source, not in the log. With `Enabled` false, nothing is registered: no
exporter for logs, traces or metrics.

## Health checks

| Route | Checks | Use it for |
|---|---|---|
| `/health` | nothing: the process answers | liveness, the Compose and Dockerfile healthchecks |
| `/health/ready` | the database, and whether shutdown has started | readiness, routing traffic |

Both are anonymous and outside the rate limiter. Readiness turns unhealthy the moment shutdown
starts, so an orchestrator stops sending traffic while `Shutdown:Timeout` drains what arrived.
Liveness touches no dependency, so a brief database outage never restarts a healthy process.
Neither says whether the jobs run: the `iterations` counters do.
<!--#if (aspire)-->

## In the Aspire dashboard

The app host turns `OpenTelemetry:Enabled` on for every host it starts, and sets
`OpenTelemetry:OtlpEndpoint`, `OpenTelemetry:OtlpProtocol` and `OpenTelemetry:ServiceName` to its
dashboard's OTLP endpoint and protocol and to the resource's name: the logs, traces and metrics of a
local run land in the dashboard with no setting of yours, and Compose keeps exporting nothing.
[Running the app host](../guides/running-the-app-host.md) says how to start it.
<!--#endif-->

## Configure

| Section | What it sets |
|---|---|
| [`OpenTelemetry`](../reference/configuration.md#opentelemetry) | the switch for the logs, traces and metrics, the OTLP endpoint and protocol, the service name, the trace sampling ratio |
| [`Shutdown`](../reference/configuration.md#shutdown) | how long in-flight requests drain |
| [Standard ASP.NET Core keys](../reference/configuration.md#standard-aspnet-core-keys) | `Logging:LogLevel:*` |

## Decisions

- [0001 Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): OpenTelemetry over OTLP.
