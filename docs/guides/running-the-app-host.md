---
type: guide
title: Running the app host
description: How to run the local stack from the Aspire app host, with every host's logs, traces and metrics in one dashboard.
---

# Running the app host

## When

You want the local stack from one command, with every host's logs, traces and metrics in one
dashboard and a debugger able to attach to each host: `src/AppHost/AT.AppHost`, an Aspire app host,
starts what `docker compose up` starts. Compose stays the reference of the deployment and the app
host follows it ([ADR 0032](../decisions/0032-the-app-host-follows-compose.md)): it reads each
container's image from the Compose files when it starts, and `LocalStackParityTests` fails when a
service or a host setting of the Compose files is missing from the app host, when the app host
starts something Compose does not, or when a web app listens on another port than its
`src/Web/apps/<App>/compose.yml` publishes.

Two descriptions of one stack drift unless one is the reference and a test holds the other to it.
Compose is the reference because it is the shape a deployment copies: generating Compose from the
app host would make every change to an image or a setting go through a generator the reviewers do not
read, and copying the images into the app host would fail every Dependabot pull request on a Compose
image until someone copied the reference across. `LocalStackParityTests` lists by name, with its
reason, each service, setting or variable in which the two stacks differ. The dashboard and the
orchestrator come from the Aspire CLI the app host's SDK pins, run through `dnx`, with no workload
and no global tool installed. SQLite is refused with the app host: its file lives in a volume Compose
shares between containers, which the processes the app host starts cannot share. The app host
becomes the reference only once Aspire can import a Compose file as its model, or the deployment
moves away from Compose.

| Resource | Started as | Compose service |
|---|---|---|
| <!--#if (postgreSql)--> |
| `db` | a container, the PostgreSQL image `docker-compose.database.yml` pins, on port 5432 | `db` |
| <!--#endif--> |
| <!--#if (sqlServer)--> |
| `db` | a container, the SQL Server image `docker-compose.database.yml` pins, on port 1433 | `db` |
| <!--#endif--> |
| `mailpit` | a container, the mail catcher, its web UI on port 8025 and SMTP on 1025 | `mailpit` |
| <!--#if (files)--> |
| `objectstore` | a container, SeaweedFS with its bucket, on port 9000 | `objectstore` |
| <!--#endif--> |
| <!--#if (api)--> |
| `api` | a process, on port 8080, once the containers are healthy | `api` |
| <!--#endif--> |
| `worker` | a process, once the API is healthy, or the containers without the API | `worker` |
| `presentation-<name>` | a process for each portal and MCP server, on a port the dashboard shows | `presentation-<name>` |
| `web-<name>` | a container built from the app's `Dockerfile`, relaying to the API, on the port `cleanarch.json` records for the app | `web-<name>` |

The app host reads the hosts and the web apps from `cleanarch.json`, so a host or an app the project
gains later starts with the others; a desktop app is left to its own start. Each host gets the
settings `docker-compose.yml` gives it, its connection string pointed at the app host's database,
and `OpenTelemetry:Enabled` with the dashboard's OTLP endpoint, so its telemetry lands there.

It differs from Compose in three ways. The hosts run on this machine in `Development`, not in their
images. The containers keep no data between runs, so each run starts from an empty database. The
fixed ports are the ones Compose binds, so the two stacks do not run at the same time.

The secrets are Aspire parameters, never values in the code:

| Parameter | Holds | Its value |
|---|---|---|
| `db-password` | the database password | generated on the first run |
| <!--#if (localAuth)--> |
| `jwt-key` | the key that signs the access tokens | generated on the first run |
| `seed-enabled`, `seed-admin-password` | the development administrator, off by default | `false`, and a generated password |
| <!--#endif--> |
| <!--#if (files)--> |
| `storage-secret-access-key` | the object store's secret key | generated on the first run |
| <!--#endif--> |
| `email-password` | the SMTP password, which Mailpit does not ask for | empty |
| <!--#if (externalAuth)--> |
| `external-auth-authority`, `external-auth-audience` | your OpenID Connect provider and this API's identifier there | asked for by the dashboard |
| `external-auth-interactive-client-id`, `external-auth-interactive-client-secret` | the client a portal or a server-rendered web app signs people in with | empty until you set them |
| `external-auth-browser-client-id` | the public client a Blazor WebAssembly app signs people in with, served to it as `SIGN_IN_CLIENT_ID` beside the provider as `SIGN_IN_AUTHORITY` | empty until you set it |
<!--#endif-->

A generated value is kept in the app host's user secrets, so it survives a restart.

## Steps

1. Start Docker, then run the app host:

   ```bash
   dotnet run --project src/AppHost/AT.AppHost
   ```

   Its first build fetches, through `dnx`, the Aspire CLI at the SDK's version, which `global.json`
   pins, with the dashboard and the orchestrator; it needs the network once per version.
2. Open the dashboard at the address the run prints, `http://localhost:15180` with its sign-in token.
<!--#if (externalAuth)-->
3. Give the provider's parameters a value, in the dashboard when it asks, or once for all runs:

   ```bash
   dotnet user-secrets set "Parameters:external-auth-authority" "<your issuer URL>" --project src/AppHost/AT.AppHost
   dotnet user-secrets set "Parameters:external-auth-audience" "<this API's identifier>" --project src/AppHost/AT.AppHost
   ```
<!--#endif-->

## Check

The dashboard lists every resource as running, healthy where it has a health check.
<!--#if (api)-->
`curl http://localhost:8080/health` answers `200`.
<!--#endif-->
`LocalStackTests`, in
`tests/Integration/AT.AppHost.IntegrationTests`, proves the same against Docker on every run of the
integration suites:

```bash
dotnet test --project tests/Integration/AT.AppHost.IntegrationTests
```

## Undo

Stop the run with Ctrl+C: the hosts stop, and the containers and their network are removed. To
forget the generated secrets as well:

```bash
dotnet user-secrets clear --project src/AppHost/AT.AppHost
```
