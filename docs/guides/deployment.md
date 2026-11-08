---
type: guide
title: Deployment
description: How to ship this project to an environment other than your machine, from its images and migrations to its secrets and instances.
---

# Deployment

## When

<!--#if (serverDatabase && !jobsWorker)-->
The API goes to an environment other than your machine. It ships as one image that serves HTTP and
runs the periodic jobs; the schema changes in a step of its own before the image starts.
<!--#endif-->
<!--#if (api && jobsWorker)-->
The API goes to an environment other than your machine. It ships as two images from one commit: the
API's, which serves HTTP, and the worker's, which runs the periodic jobs; the schema changes in a step
of its own before either starts.
<!--#endif-->
<!--#if (!api)-->
This project holds no API. Its hosts go to an environment other than your machine, each as an image
of its own from one commit; the schema changes in a step of its own before any of them starts.
<!--#endif-->
<!--#if (sqlite)-->
This project is not deployed. It runs on SQLite, a development database, and the API refuses to start
outside Development, for the reasons below, which also give the move to a server database. The rest
of this page applies once the move is made.

A file has one writer at a time, its locks live in the process that opened it, and its types are
fewer than a server's: a project that started anywhere would meet these limits in production, under
load, rather than on its first start. The host throws at composition when its environment is not
`Development`, with a message that names the way out, and no setting lifts the refusal: every
deployment would set it, and the refusal would stop saying anything. No release workflow and no
migration bundle are generated, since there is nothing to deploy.

To deploy, generate the project again with `--database postgresql` or `sqlserver`, move the
features' code across, and let `bootstrap` create the migrations for the new provider. The domain and
the application do not change, since no provider reaches them.
<!--#endif-->

## The local stack is no model

`docker-compose.yml` and `.env.example` run the whole project on one machine, for one developer, and
each of their values trades safety for a start that needs nothing. A deployment copies none of them:

| The local stack | A deployment |
|---|---|
| <!--#if (api)--> |
| `ASPNETCORE_ENVIRONMENT=Development`, under which the API applies its migrations at start and serves its OpenAPI document and reference to anyone | `Production`, the default when the variable is unset |
| <!--#endif--> |
| <!--#if (!api)--> |
| `ASPNETCORE_ENVIRONMENT=Development`, under which the hosts relax what they refuse elsewhere | `Production`, the default when the variable is unset |
| <!--#endif--> |
| <!--#if (serverDatabase)--> |
| a database in a container, its password in `.env.example` for anyone to read, one principal for everything | a database you operate or rent, its password from a secret store, and a migration principal apart from the runtime one, as [migrations at release](#migrations-at-release) says |
| <!--#endif--> |
| <!--#if (sqlite)--> |
| SQLite, in a file the containers share | a server database, as [when](#when) says |
| <!--#endif--> |
| Mailpit, which catches every mail, reached without TLS (`EMAIL_SECURITY=None`, `EMAIL_ALLOW_INSECURE_TRANSPORT=true`) | a relay over `StartTls` or `SslOnConnect`, with `Email:AllowInsecureTransport` false |
| plain HTTP on ports bound to `127.0.0.1` | TLS at the proxy in front, as [behind a proxy](#behind-a-proxy) says |
| <!--#if (api)--> |
| `Cors:AllowedOrigins` empty, `AllowedHosts` at `*` | an origin only for a browser that calls the API from another origin, and your own host names |
| <!--#endif--> |
| <!--#if (!api)--> |
| `AllowedHosts` at `*` | your own host names |
| <!--#endif--> |
| <!--#if (localAuth)--> |
| the public development signing key, which the hosts refuse outside Development | a key of your own from the secret store; [rotating the signing key](rotating-the-signing-key.md) replaces it later |
| passwords of 8 characters with no class required, a lockout of 1 minute after 10 failures, access tokens of 60 minutes, `JWT_REQUIRE_HTTPS_METADATA=false` | the defaults of [the configuration reference](../reference/configuration.md#identity-and-tokens), which `.env.example` overrides |
| mailed links that open `http://localhost:8080` | the pages of your own front end |
| <!--#endif--> |
| <!--#if (files)--> |
| an object store with a fixed access key, reached without TLS (`STORAGE_ALLOW_INSECURE_TRANSPORT=true`) | a bucket reached over TLS, with an instance role and no key in the configuration |
<!--#endif-->

<!--#if (api)-->
## The image

```bash
docker build -f src/Presentation/AT.Api/Dockerfile -t app-template-api:local .
```

The build context is the repository root, because restore needs `Directory.Build.props`,
`Directory.Packages.props` and `global.json`. The SDK stage builds portable IL on the builder's
platform; the runtime is ASP.NET on alpine, both pinned by digest. The image runs as a non-root user
(`USER $APP_UID`, which alpine does not set by itself), speaks HTTP on 8080 only, and carries a
`HEALTHCHECK` on `/health` that needs alpine's `wget`. `.dockerignore` keeps
`appsettings.Development.json`, `.git`, `tests/` and keys out of the layers.

The `Dockerfile` copies each project file by name before restoring, so a project the API references
needs its `COPY` line there too; the build passes locally without it, and only the image fails.

<!--#endif-->
<!--#if (!api)-->
## The image

```bash
docker build -f src/Presentation/<Initials>.Worker/Dockerfile -t app-template-worker:local .
```

Each host has a `Dockerfile` in its own folder under `src/Presentation`, built the same way: the worker
above, and a portal or an MCP server when the project holds one. The build context is the repository
root, because restore needs `Directory.Build.props`, `Directory.Packages.props` and `global.json`. The
SDK stage builds portable IL on the builder's platform; the runtime is ASP.NET on alpine, both pinned
by digest, and each image runs as a non-root user (`USER $APP_UID`, which alpine does not set by
itself). The worker serves no port and carries no `HEALTHCHECK`: its log and its loops' `iterations`
counter say it runs ([Background jobs](../features/background-jobs.md#several-instances)). A portal or
an MCP server speaks HTTP on 8080 only and carries a `HEALTHCHECK` on `/health` that needs alpine's
`wget`. `.dockerignore` keeps `appsettings.Development.json`, `.git`, `tests/` and keys out of the
layers.

Each `Dockerfile` copies each project file by name before restoring, so a project a host references
needs its `COPY` line there too; the build passes locally without it, and only the image fails.

<!--#endif-->
<!--#if (anyWeb)-->
Each web app has its own image, built from `src/Web`: nginx serves a single-page app, and a
server-rendered app runs on its own Node server, as one instance; [the web front end](../features/web-frontend.md#images-and-headers)
describes them.

<!--#endif-->
<!--#if (api && jobsWorker)-->
The worker's image is built the same way, from `src/Presentation/<Initials>.Worker/Dockerfile`, on the
same runtime and user. It serves no port and carries no `HEALTHCHECK`: its log and its loops'
`iterations` counter say it runs ([Background jobs](../features/background-jobs.md#several-instances)).
Release both images together, so a pass and the route that shares its lease spell the lease's name
the same.

<!--#endif-->
<!--#if (serverDatabase)-->
## Migrations at release

<!--#if (api)-->
Outside Development the API never touches its schema. Apply migrations before starting a version that
expects them, with a database principal that holds DDL rights; the API's runtime user holds none.
<!--#endif-->
<!--#if (!api)-->
No host of this project touches its schema. Apply migrations before starting a version that expects
them, with a database principal that holds DDL rights; the hosts' runtime users hold none.
<!--#endif-->

```bash
dotnet cleanarch migrations bundle
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

Running migrations at release is a row of [the stack](../architecture/README.md#the-stack).
Moving them into the process would give the runtime user DDL rights and race every replica on the
history table.

<!--#endif-->
## Configuration and secrets

Configuration loads `appsettings.json`, then `appsettings.Development.json` in Development only, then
user secrets, then environment variables.
<!--#if (api)-->
A deployment supplies every blank of `appsettings.json` as an environment variable, sections joined by
`__`: `ConnectionStrings__Default`, `Cors__AllowedOrigins__0`.
<!--#endif-->
<!--#if (!api)-->
A deployment supplies every blank of `appsettings.json` as an environment variable, sections joined by
`__`: `ConnectionStrings__Default`.
<!--#endif-->
<!--#if (localAuth)-->
`Jwt__Key` is one of them.
<!--#endif-->
<!--#if (externalAuth)-->
`Authentication__External__Authority` and `Authentication__External__Audience` are two of them.
<!--#endif-->

<!--#if (api)-->
Every section is validated at start: a missing or out-of-range value stops the host before it binds a
port, naming each key that failed. Take secrets from a secret manager, never from a tracked file.
Narrow `AllowedHosts` from `*` to the hosts you serve, and set `Cors:AllowedOrigins` only when a browser calls the API from another origin.
[The configuration reference](../reference/configuration.md) lists every key.
<!--#endif-->
<!--#if (!api)-->
Every section is validated at start: a missing or out-of-range value stops the host before it runs,
naming each key that failed. Take secrets from a secret manager, never from a tracked file. Narrow
`AllowedHosts` from `*` to the hosts you serve, in each `appsettings.json` that holds it.
[The configuration reference](../reference/configuration.md) lists every key.
<!--#endif-->
<!--#if (localAuth)-->
[Rotating the signing key](rotating-the-signing-key.md) replaces `Jwt:Key` without signing anyone
out.
<!--#endif-->

### A secrets vault

A vault that the platform does not turn into environment variables joins as one more configuration
source, an `IConfigurationSource` from its vendor's package: Azure Key Vault's, AWS Systems Manager's
or a HashiCorp Vault provider, for instance. This project ships none.

1. Pin the package in `Directory.Packages.props` and reference it from each host that reads a secret
   the vault holds; the advisory gate covers it as any other package.
2. In each of those hosts' `Program.cs`, add the source right after the builder is created, before
   the first `builder.Services` call: a module reads some values as it registers, the connection
   string among them, and a source added after it is too late for that value.
3. Mind the order: a source added there comes after the environment variables and the command line,
   so the vault's value wins. Call `builder.Configuration.AddEnvironmentVariables()` again after it
   when an environment variable must still override the vault.
4. Store each secret under the key the configuration reference names: the provider maps its own
   separator to `:`, which its documentation gives.
5. Give the process the vault's address and its identity from the platform, a managed identity or an
   instance role, never a credential in a file; the address is the one value the vault cannot hold.

The validators then judge the merged configuration at start, so a required secret the vault alone
holds passes, and one it lacks stops the host and names its key. A provider that cannot reach its
vault throws as the configuration loads, before any validator runs, unless it is marked optional.
Each options class is read once, at start: a secret changed in the vault reaches a host when it
restarts. No value is logged: never log an options object or a dump of the configuration.

## Behind a proxy

<!--#if (!api)-->
TLS terminates upstream, at the ingress or the load balancer. A portal or an MCP server, when the
project holds one, speaks plain HTTP on 8080, has no HTTPS redirection and sends no HSTS: both belong
to the component that owns the domain, and a redirect would answer the orchestrator's health probe
with `307`. Enforce HTTPS and send HSTS there. The worker serves no HTTP.
<!--#endif-->
<!--#if (api)-->
TLS terminates upstream, at the ingress or the load balancer. The API has no HTTPS redirection and
sends no HSTS: both belong to the component that owns the domain, and a redirect would answer the
orchestrator's health probe with `307`. Enforce HTTPS and send HSTS there.

Forwarded headers are believed only from proxies you name. Until the `ReverseProxy` section is
configured, the API reads the proxy's address as the client's, so every caller shares one rate-limit
window. Set `ReverseProxy:Enabled`, list the hops in `KnownProxies` or `KnownNetworks`, and set
`ForwardLimit` to their number. The trust set ships empty, and the validator refuses `Enabled` with
nothing trusted, which ASP.NET Core would read as trusting everyone.
<!--#endif-->

## Several instances

| Concern | What to do |
|---|---|
| Background jobs | `Jobs:Enabled` is `true` on one instance and `false` on the others; a pass that must not run twice also takes a lease |
| <!--#if (api)--> |
| Rate limiting | each instance counts its own window, so the effective limit is the limit times the instances |
| <!--#endif--> |
| Cache | `HybridCache` is in process; a shared level is an `IDistributedCache` registration |
| <!--#if (postgreSql)--> |
| Connection pool | keep instances × `Database:MaxPoolSize` under the server's `max_connections` |
| <!--#endif--> |
| <!--#if (sqlServer)--> |
| Connection pool | keep instances × `Database:MaxPoolSize` under the server's connection limit |
| <!--#endif--> |
| <!--#if (sqlite)--> |
| Instances | run one: the leases are in process and the file has one writer |
| <!--#endif--> |
| <!--#if (localAuth)--> |
| Mailed tokens | the Data Protection key ring is persisted with the identity tables, so every instance reads the same keys |
<!--#endif-->

[Background jobs](../features/background-jobs.md#several-instances) covers the leases.

## Shutting down

<!--#if (!api)-->
On `SIGTERM` a host stops its loops and requests through the generic host's shutdown, which waits up to
its timeout, 30 seconds by default, for them to finish. Give the orchestrator a grace period longer
than that timeout.
<!--#endif-->
<!--#if (api)-->
On `SIGTERM`, `/health/ready` turns unhealthy at once, so the orchestrator stops routing, and the host
waits up to `Shutdown:Timeout` (30 seconds by default) for in-flight requests. `/health`, the liveness
probe, stays healthy while the process drains. Give the orchestrator a grace period longer than that
timeout.
<!--#endif-->

<!--#if (ci && serverDatabase)-->
## Verifying an image

The release workflow attests the provenance of each image it pushes to GHCR, signed with the
identity of the workflow run that built it, and attaches BuildKit's SBOM and provenance to the image
itself. Before deploying an image, check that this repository's release built it:

```bash
gh attestation verify oci://ghcr.io/<owner>/<repository>/<component>@<digest> --repo <owner>/<repository>
```

`<component>` is the image's name in the release matrix of `.github/workflows/release.yml`, and
`<digest>` the digest the release's summary prints, with the exact command. The check answers with
the workflow and the commit that built the image, and fails for an image this repository's workflow
did not attest. A private package needs `docker login ghcr.io` first. Deploy by digest, which a tag
moved later cannot change.

<!--#endif-->
## Before you go live

[`SECURITY.md`](../../SECURITY.md) lists what a deployment must still do, and what this project does not
protect.
