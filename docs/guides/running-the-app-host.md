# Running the app host

## When

You want the local stack from one command, with every host's logs, traces and metrics in one
dashboard and a debugger able to attach to each host: `Src/AppHost/AT.AppHost`, an Aspire app host,
starts what `docker compose up` starts. Compose stays the reference of the deployment and the app
host follows it ([ADR 0032](../decisions/0032-the-app-host-follows-compose.md)): it reads each
container's image from the Compose files when it starts, and `LocalStackParityTests` fails when a
service or a host setting of the Compose files is missing from the app host, when the app host
starts something Compose does not, or when a web app listens on another port than its
`Src/Web/apps/<App>/compose.yml` publishes.

| Resource | Started as | Compose service |
|---|---|---|
<!--#if (postgreSql)-->
| `db` | a container, the PostgreSQL image `docker-compose.database.yml` pins, on port 5432 | `db` |
<!--#endif-->
<!--#if (sqlServer)-->
| `db` | a container, the SQL Server image `docker-compose.database.yml` pins, on port 1433 | `db` |
<!--#endif-->
| `mailpit` | a container, the mail catcher, its web UI on port 8025 and SMTP on 1025 | `mailpit` |
<!--#if (files)-->
| `objectstore` | a container, SeaweedFS with its bucket, on port 9000 | `objectstore` |
<!--#endif-->
<!--#if (api)-->
| `api` | a process, on port 8080, once the containers are healthy | `api` |
<!--#endif-->
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
<!--#if (localAuth)-->
| `jwt-key` | the key that signs the access tokens | generated on the first run |
| `seed-enabled`, `seed-admin-password` | the development administrator, off by default | `false`, and a generated password |
<!--#endif-->
<!--#if (files)-->
| `storage-secret-access-key` | the object store's secret key | generated on the first run |
<!--#endif-->
| `email-password` | the SMTP password, which Mailpit does not ask for | empty |
<!--#if (externalAuth)-->
| `external-auth-authority`, `external-auth-audience` | your OpenID Connect provider and this API's identifier there | asked for by the dashboard |
| `external-auth-interactive-client-id`, `external-auth-interactive-client-secret` | the client a portal or a server-rendered web app signs people in with | empty until you set them |
<!--#endif-->

A generated value is kept in the app host's user secrets, so it survives a restart.

## Steps

1. Start Docker, then run the app host:

   ```bash
   dotnet run --project Src/AppHost/AT.AppHost
   ```

   Its first build fetches, through `dnx`, the Aspire CLI at the SDK's version, which `global.json`
   pins, with the dashboard and the orchestrator; it needs the network once per version.
2. Open the dashboard at the address the run prints, `http://localhost:15180` with its sign-in token.
<!--#if (externalAuth)-->
3. Give the provider's parameters a value, in the dashboard when it asks, or once for all runs:

   ```bash
   dotnet user-secrets set "Parameters:external-auth-authority" "<your issuer URL>" --project Src/AppHost/AT.AppHost
   dotnet user-secrets set "Parameters:external-auth-audience" "<this API's identifier>" --project Src/AppHost/AT.AppHost
   ```
<!--#endif-->

## Check

The dashboard lists every resource as running, healthy where it has a health check.
<!--#if (api)-->
`curl http://localhost:8080/health` answers `200`.
<!--#endif-->
`LocalStackTests`, in
`Tests/Integration/AT.AppHost.IntegrationTests`, proves the same against Docker on every run of the
integration suites:

```bash
dotnet test --project Tests/Integration/AT.AppHost.IntegrationTests
```

## Undo

Stop the run with Ctrl+C: the hosts stop, and the containers and their network are removed. To
forget the generated secrets as well:

```bash
dotnet user-secrets clear --project Src/AppHost/AT.AppHost
```
