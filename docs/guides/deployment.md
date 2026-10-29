# Deployment

## When

<!--#if (serverDatabase && !jobsWorker)-->
The API goes to an environment other than your machine. It ships as one image that serves HTTP and
runs the periodic jobs; the schema changes in a step of its own before the image starts.
<!--#endif-->
<!--#if (jobsWorker)-->
The API goes to an environment other than your machine. It ships as two images from one commit: the
API's, which serves HTTP, and the worker's, which runs the periodic jobs; the schema changes in a step
of its own before either starts.
<!--#endif-->
<!--#if (sqlite)-->
This project is not deployed. It runs on SQLite, a development database, and the API refuses to start
outside Development; [ADR 0021](../decisions/0021-sqlite-is-a-development-database.md) says why and
how to move to a server database. The rest of this page applies once the move is made.
<!--#endif-->

## The image

```bash
docker build -f Src/Presentation/AppTemplate.Api/Dockerfile -t app-template-api:local .
```

The build context is the repository root, because restore needs `Directory.Build.props`,
`Directory.Packages.props` and `global.json`. The SDK stage builds portable IL on the builder's
platform; the runtime is ASP.NET on alpine, both pinned by digest. The image runs as a non-root user
(`USER $APP_UID`, which alpine does not set by itself), speaks HTTP on 8080 only, and carries a
`HEALTHCHECK` on `/health` that needs alpine's `wget`. `.dockerignore` keeps
`appsettings.Development.json`, `.git`, `Tests/` and keys out of the layers.

The `Dockerfile` copies each project file by name before restoring, so a project the API references
needs its `COPY` line there too; the build passes locally without it, and only the image fails.

<!--#if (jobsWorker)-->
The worker's image is built the same way, from `Src/Presentation/<Project>.Worker/Dockerfile`, on the
same runtime and user. It serves no port and carries no `HEALTHCHECK`: its log and its loops'
`iterations` counter say it runs ([Background jobs](../features/background-jobs.md#several-instances)).
Release both images together, so a pass and the route that shares its lease spell the lease's name
the same.

<!--#endif-->
<!--#if (anyWeb)-->
Each web app has its own image, built from `Src/Web`: nginx serves a single-page app, and a
server-rendered app runs on its own Node server, as one instance; [the web front end](../features/web-frontend.md#images-and-headers)
describes them.

<!--#endif-->
<!--#if (serverDatabase)-->
## Migrations at release

Outside Development the API never touches its schema. Apply migrations before starting a version that
expects them, with a database principal that holds DDL rights; the API's runtime user holds none.

```bash
dotnet run Tools/Tasks.cs migration-bundle
```

<!--#if (localAuth)-->
It writes two self-contained bundles under `artifacts/migrate`: `efbundle-identity` for the
`identity` schema and `efbundle` for the business one. Run both, authentication first; applying one
alone leaves half the schema absent.
<!--#endif-->
<!--#if (!localAuth)-->
It writes one self-contained bundle, `artifacts/migrate/efbundle`.
<!--#endif-->
A bundle holds the migrations the commit carries, from the initial one bootstrap created onwards.
Each carries no connection string and needs no SDK on the target:

<!--#if (postgreSql)-->
```bash
./efbundle --connection "Host=…;Database=…;Username=<migration user>;Password=…"
```
<!--#endif-->
<!--#if (sqlServer)-->
```bash
./efbundle --connection "Server=…;Database=…;User Id=<migration user>;Password=…;TrustServerCertificate=True"
```
<!--#endif-->

<!--#if (ci)-->
The release workflow builds the bundles from the tagged commit and uploads them as artifacts;
running them is the deployment's step.
<!--#endif-->

Running migrations at release is a row of [ADR 0001](../decisions/0001-adopt-the-generated-baseline.md).
Moving them into the process would give the runtime user DDL rights and race every replica on the
history table.
<!--#endif-->

## Configuration and secrets

Configuration loads `appsettings.json`, then `appsettings.Development.json` in Development only, then
user secrets, then environment variables. A deployment supplies every blank of `appsettings.json` as
an environment variable, sections joined by `__`: `ConnectionStrings__Default`,
`Cors__AllowedOrigins__0`.
<!--#if (localAuth)-->
`Jwt__Key` is one of them.
<!--#endif-->
<!--#if (externalAuth)-->
`Authentication__External__Authority` and `Authentication__External__Audience` are two of them.
<!--#endif-->

Every section is validated at start: a missing or out-of-range value stops the host before it binds a
port, naming each key that failed. Take secrets from a secret manager, never from a tracked file.
Narrow `AllowedHosts` from `*` to the hosts you serve, and set `Cors:AllowedOrigins` to your origins.
[The configuration reference](../reference/configuration.md) lists every key.

## Behind a proxy

TLS terminates upstream, at the ingress or the load balancer. The API has no HTTPS redirection and
sends no HSTS: both belong to the component that owns the domain, and a redirect would answer the
orchestrator's health probe with `307`. Enforce HTTPS and send HSTS there.

Forwarded headers are believed only from proxies you name. Until the `ReverseProxy` section is
configured, the API reads the proxy's address as the client's, so every caller shares one rate-limit
window. Set `ReverseProxy:Enabled`, list the hops in `KnownProxies` or `KnownNetworks`, and set
`ForwardLimit` to their number. The trust set ships empty, and the validator refuses `Enabled` with
nothing trusted, which ASP.NET Core would read as trusting everyone.

## Several instances

| Concern | What to do |
|---|---|
| Background jobs | `Jobs:Enabled` is `true` on one instance and `false` on the others; a pass that must not run twice also takes a lease |
| Rate limiting | each instance counts its own window, so the effective limit is the limit times the instances |
| Cache | `HybridCache` is in process; a shared level is an `IDistributedCache` registration |
<!--#if (postgreSql)-->
| Connection pool | keep instances × `Database:MaxPoolSize` under the server's `max_connections` |
<!--#endif-->
<!--#if (sqlServer)-->
| Connection pool | keep instances × `Database:MaxPoolSize` under the server's connection limit |
<!--#endif-->
<!--#if (sqlite)-->
| Instances | run one: the leases are in process and the file has one writer |
<!--#endif-->
<!--#if (localAuth)-->
| Mailed tokens | the Data Protection key ring is persisted with the identity tables, so every instance reads the same keys |
<!--#endif-->

[Background jobs](../features/background-jobs.md#several-instances) covers the leases.

## Shutting down

On `SIGTERM`, `/health/ready` turns unhealthy at once, so the orchestrator stops routing, and the host
waits up to `Shutdown:Timeout` (30 seconds by default) for in-flight requests. `/health`, the liveness
probe, stays healthy while the process drains. Give the orchestrator a grace period longer than that
timeout.

## Before you go live

[`SECURITY.md`](../../SECURITY.md) lists what a deployment must still do, and what this project does not
protect.
