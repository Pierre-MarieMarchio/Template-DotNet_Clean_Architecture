---
type: guide
title: First run
description: How to go from a fresh clone to a running stack, bootstrapped once, and see its hosts answer.
---

# First run

<!--#if (canAddFeature && api)-->
From a fresh clone to a running stack, an authenticated call and a first feature of your own.
<!--#endif-->
<!--#if (!canAddFeature && api)-->
From a fresh clone to a running stack and a first call to the API.
<!--#endif-->
<!--#if (!api)-->
From a fresh clone to a running stack whose hosts answer.
<!--#endif-->
Run every command from the repository root.

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| .NET SDK | 10.0.100 or any later .NET 10 SDK, as `global.json` allows | building, running, every task |
| Docker with Compose v2 | any recent | the local stack and the integration tests |
| <!--#if (anyWeb)--> |
| Node.js | the version in `Src/Web/.node-version` | the web workspace |
| pnpm | the version `Src/Web/package.json` pins, run by Corepack | the web workspace |
<!--#endif-->

```bash
dotnet --version
```

On Windows, a path longer than 260 characters fails (`Filename too long` from git, a missing file
from a build) unless long paths are on. The deepest file of this project is about 170 characters
below its root, so clone it in a folder shorter than about 90 characters, such as `C:\src\`, or turn
long paths on once per machine: set `LongPathsEnabled` in the registry, as
[Microsoft's page on the path limit](https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation)
says, and run `git config --global core.longpaths true`.

<!--#if (anyWeb)-->
Run `corepack enable` once per machine so the pinned pnpm is the one that runs.

<!--#endif-->
## Bootstrap once

Run it right after generating, as `dotnet new` ends by saying. It needs the network, to restore the
packages and the local tools, and not Docker.

```bash
dotnet run Tools/Tasks.cs bootstrap
```

Commit what it changes before anything else.
<!--#if (presentationBlazorServer)-->
It creates the portal's pages with `dotnet new blazor` and adds the project to the solution
([Presentation projects](../features/presentation.md#creating-the-project)).
<!--#endif-->
<!--#if (presentationAvalonia)-->
It creates the desktop app's views with `dotnet new avalonia.mvvm` and adds the project to the
solution ([Presentation projects](../features/presentation.md#creating-the-project)), and last
writes the .NET client from the OpenAPI reference.
<!--#endif-->
<!--#if (presentationMcp)-->
It adds the MCP server, which comes written whole, and its tests to the solution
([Presentation projects](../features/presentation.md#creating-the-project)).
<!--#endif-->
<!--#if (presentationWpf)-->
It adds the desktop app and its tests to the solution
([Presentation projects](../features/presentation.md#creating-the-project)), and last writes the
.NET client from the OpenAPI reference.
<!--#endif-->
<!--#if (jobsWorker)-->
It also adds the worker's project in `Src/Presentation`, written in the shape of `dotnet new worker`,
to the solution with its tests.
<!--#endif-->
<!--#if (api)-->
It formats the solution, writes the index of each folder under `docs/` from its pages, writes the
OpenAPI reference the contract test reads
([HTTP API](../architecture/http-api.md#versioning-and-the-openapi-document)), pins this project's
own tool, `dotnet cleanarch`, at the version of the template it came from, restores the local tools
(`dotnet-ef` among them), then creates each database context's initial migration under its
project's `Migrations` folder.
<!--#endif-->
<!--#if (!api)-->
It formats the solution, writes the index of each folder under `docs/` from its pages, pins this
project's own tool, `dotnet cleanarch`, at the version of the template it came from, restores the
local tools (`dotnet-ef` among them), then creates each database context's initial migration under
its project's `Migrations` folder.
<!--#endif-->
[The project tool](../reference/cli.md) lists the commands `dotnet cleanarch` offers from then on.
The project ships no migration: the first one is written for this project's own model, and it is
the project's from the commit on ([migrations](../guides/migrations.md#the-initial-migration)).
<!--#if (webFramework)-->
It then writes the .NET client where the web app calls the API through it, and creates the app as the
scaffold file in its folder describes, with the generator that file names.
<!--#endif-->
<!--#if (anyWeb)-->
It last installs the web workspace and generates its TypeScript API client, which needs Node.js and
pnpm.
<!--#endif-->

Both steps are required. Using directives sort around the project's own name, so their order is only
right once the project has its name. The build does not check that order: until bootstrap runs,
`dotnet run Tools/Tasks.cs format` fails, and `verify` with it.
<!--#if (api)-->
Without a migration, the API cannot create its schema, the migration drift tests fail, and `verify`
refuses to start.
<!--#endif-->
<!--#if (!api)-->
Without a migration, no schema can be created, the migration drift tests fail, and `verify` refuses
to start.
<!--#endif-->
<!--#if (ci)-->
It is also the first step of CI, so a first push would fail.
<!--#endif-->

A project generated by `dotnet new` is not a git repository yet: run `git init` and make the first
commit of the generated files before bootstrap, so that the next commit holds only what bootstrap
wrote. `dnx CleanArch.Create -- new --setup` does the `git init`, both commits, bootstrap and the
`.env` copy for you.

When bootstrap stops, its last message says which step failed and what is left undone, and what it
already did stays done. A project made by the launcher also keeps bootstrap's full output in
`artifacts/bootstrap.log`, which git ignores. Run bootstrap again once the cause is fixed. If the
step that failed is the install of `CleanArch.Cli` at the version of the template this project came
from, that version is not on a NuGet source the machine uses: nuget.org holds only published
releases, so a version not released yet, or a template packed locally, has no package there. Put the
package `CleanArch.Cli.<version>.nupkg`, packed with `dotnet pack` from the template's
repository, in a folder, install it from there with
`dotnet tool install CleanArch.Cli --local --version <version> --add-source <folder>`, then run
bootstrap again.

## Run with Compose

```bash
cp .env.example .env
docker compose up -d --build --wait
```

<!--#if (api)-->
<!--#if (externalAuth)-->
Before starting, set `EXTERNAL_AUTH_AUTHORITY` (your provider's issuer URL) and
`EXTERNAL_AUTH_AUDIENCE` (this API's identifier there) in `.env`: the API refuses to start without
them.

<!--#endif-->
`.env` is git-ignored; the defaults in `.env.example` work on localhost. Compose stops at once on a
missing variable rather than substituting an empty one, and `--wait` returns when every service is
healthy. The API runs in Development, so it applies its migrations at start.

| Service | Address |
|---|---|
| API | <http://localhost:8080> |
| Scalar, the OpenAPI UI (Development only) | <http://localhost:8080/scalar/v1> |
| <!--#if (presentationBlazorServer)--> |
| The portal | <http://localhost:8090> |
| <!--#endif--> |
| <!--#if (presentationMcp)--> |
| The MCP server | <http://localhost:8091/mcp> |
| <!--#endif--> |
| Mailpit, where mails land | <http://localhost:8025> |
| <!--#if (postgreSql)--> |
| PostgreSQL | `127.0.0.1:5432` |
| <!--#endif--> |
| <!--#if (sqlServer)--> |
| SQL Server | `127.0.0.1:1433` |
| <!--#endif--> |
| <!--#if (files)--> |
| Object store (SeaweedFS, S3 API) | <http://localhost:9000> |
| <!--#endif--> |
| <!--#if (webFramework)--> |
| Web app | <http://localhost:5173> |
| <!--#endif--> |
| <!--#if (webPlaceholder)--> |
| Web app | <http://localhost:8081> |
<!--#endif-->

Every port binds to `127.0.0.1`. The API speaks plain HTTP on 8080: TLS terminates upstream, as
[deployment](../guides/deployment.md#behind-a-proxy) describes.
<!--#if (anyWeb)-->
The web app relays `/api` to the API, so a browser talks to one origin.
<!--#endif-->
<!--#if (sqlServer)-->
SQL Server runs capped at 1 GB of memory (`MSSQL_MEMORY_LIMIT_MB`); where a local SQL Server or the
host already holds port 1433, set `MSSQL_HOST_PORT` in `.env`.
<!--#endif-->

```bash
docker compose logs -f api    # follow the API
docker compose down           # stop, keep the data
docker compose down -v        # stop and delete the data
```

## Run on the host

For a debugger, start only the dependencies and run the API yourself:

<!--#if (files && serverDatabase)-->
```bash
docker compose up -d --wait db mailpit objectstore
dotnet run Tools/Tasks.cs run
```
<!--#endif-->
<!--#if (!files && serverDatabase)-->
```bash
docker compose up -d --wait db mailpit
dotnet run Tools/Tasks.cs run
```
<!--#endif-->
<!--#if (files && sqlite)-->
```bash
docker compose up -d --wait mailpit objectstore
dotnet run Tools/Tasks.cs run
```
<!--#endif-->
<!--#if (!files && sqlite)-->
```bash
docker compose up -d --wait mailpit
dotnet run Tools/Tasks.cs run
```
<!--#endif-->

The API then answers on <http://localhost:5187>.
<!--#if (serverDatabase)-->
`appsettings.Development.json` already points at the Compose database and mail sink, so nothing else
needs setting.
<!--#endif-->
<!--#if (sqlite)-->
It creates and migrates its database file, `App_Data/appdb.db`, under the directory it starts in, on first start.
`appsettings.Development.json` already points at the mail sink, so nothing else needs setting.
<!--#endif-->
[Migrations](../guides/migrations.md#your-own-database) covers your own database.
<!--#if (aspire)-->

## Run with the app host

The Aspire app host starts the same stack from one command, the hosts as processes a debugger
attaches to, and shows their logs, traces and metrics in its dashboard:

```bash
dotnet run --project Src/AppHost/AT.AppHost
```

It binds the ports Compose binds, so stop the Compose stack first with `docker compose down`.
[Running the app host](../guides/running-the-app-host.md) lists what it starts and where its secrets
come from.
<!--#endif-->

## Call the API

The readiness probe answers without a token, whichever way callers sign in, and says the API and its
database are up:

```bash
curl -s http://localhost:8080/health/ready    # http://localhost:5187 on the host
```

<!--#if (localAuth)-->
Then register, confirm the address, sign in, and read your own profile. The sign-in reads the token with
`jq`; without it, copy `tokens.accessToken` from the response into `TOKEN` by hand.

```bash
API=http://localhost:8080    # http://localhost:5187 on the host

curl -s -X POST "$API/api/v1/auth/register" \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","password":"Passw0rd!x"}'                  # 202
```

Open Mailpit at <http://localhost:8025> and the confirmation mail. Its link ends with
`#email=…&token=…`; the token is percent-encoded there, so decode it before posting it:

```bash
python3 -c 'import sys, urllib.parse as u; print(u.unquote(sys.argv[1]))' '<token from the link>'

curl -s -X POST "$API/api/v1/auth/confirm-email" \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","token":"<the decoded token>"}'            # 204

TOKEN=$(curl -s -X POST "$API/api/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","password":"Passw0rd!x"}' | jq -r .tokens.accessToken)

curl -s -H "Authorization: Bearer $TOKEN" "$API/api/v1/auth/me"            # 200
```

A sign-in before the confirmation fails with `auth.login.invalidCredentials`: an unconfirmed account
answers exactly like a wrong password. `AppTemplate.Api.http` holds the same sequence and the
requests that follow it, for the VS Code REST Client or Visual Studio.
<!--#endif-->
<!--#if (externalAuth)-->
This API has no sign-in route: every request carries a bearer token your identity provider issued for
the audience `Authentication:External:Audience`. Without a token every route answers `401`, the
health probes apart; with one, the provider's subject becomes an internal owner on its first request.
<!--#endif-->
<!--#if (externalAuth && notes)-->

```bash
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/notes?page=1&pageSize=20"
```
<!--#endif-->
<!--#if (publicApi)-->
This API authenticates nobody, and every route is public: Scalar below lists them all and sends any
of them without a token.
<!--#endif-->

## Explore with Scalar

<http://localhost:8080/scalar/v1> lists every route with its schema and lets you send requests. It and
the document behind it, `/openapi/v1.json`, exist in Development only.
<!--#if (accounts)-->
Paste an access token into its authentication field to call the protected routes.
<!--#endif-->
<!--#endif-->
<!--#if (!api)-->
<!--#if (externalAuth)-->
Before starting, set `EXTERNAL_AUTH_AUTHORITY` (your provider's issuer URL) and
`EXTERNAL_AUTH_AUDIENCE` in `.env`: the hosts refuse to start without them.

<!--#endif-->
`.env` is git-ignored; the defaults in `.env.example` work on localhost. Compose stops at once on a
missing variable rather than substituting an empty one, and `--wait` returns when every service is
healthy.

This project has no API, which is the host that applies the migrations at start in Development, and
no other host migrates. Once the database is up, apply each context's migrations from the host:

```bash
dotnet ef database update --project Src/Infrastructure/AT.Infrastructure.Persistence --startup-project Src/Infrastructure/AT.Infrastructure.Persistence
```

<!--#if (localAuth)-->
```bash
dotnet ef database update --project Src/Infrastructure/AT.Infrastructure.Auth --startup-project Src/Infrastructure/AT.Infrastructure.Auth
```

<!--#endif-->
| Service | Address |
|---|---|
| <!--#if (presentationBlazorServer)--> |
| The portal | <http://localhost:8090> |
| <!--#endif--> |
| <!--#if (presentationMcp)--> |
| The MCP server | <http://localhost:8091/mcp> |
| <!--#endif--> |
| Mailpit, where mails land | <http://localhost:8025> |
| <!--#if (postgreSql)--> |
| PostgreSQL | `127.0.0.1:5432` |
| <!--#endif--> |
| <!--#if (sqlServer)--> |
| SQL Server | `127.0.0.1:1433` |
| <!--#endif--> |
| <!--#if (files)--> |
| Object store (SeaweedFS, S3 API) | <http://localhost:9000> |
<!--#endif-->

Every port binds to `127.0.0.1`; [presentation projects](../features/presentation.md) describes each
host.

```bash
docker compose logs -f    # follow the hosts
docker compose down       # stop, keep the data
docker compose down -v    # stop and delete the data
```
<!--#endif-->

## Run the tests

<!--#if (api)-->
```bash
dotnet run Tools/Tasks.cs test                    # the solution's suites, Docker needed
dotnet run Tools/Tasks.cs test --no-integration   # without Docker
dotnet run Tools/Tasks.cs end-to-end              # the end-to-end suite, against a stack of its own
```
<!--#endif-->
<!--#if (!api)-->
```bash
dotnet run Tools/Tasks.cs test                    # the solution's suites, Docker needed
dotnet run Tools/Tasks.cs test --no-integration   # without Docker
```
<!--#endif-->

<!--#if (postgreSql)-->
The integration suites start their own PostgreSQL in a container.
<!--#endif-->
<!--#if (sqlServer)-->
The integration suites start their own SQL Server in a container.
<!--#endif-->
<!--#if (sqlite)-->
The integration suites create their own SQLite file.
<!--#endif-->
<!--#if (aspire)-->
`LocalStackTests` starts the app host's whole stack, on ports of its own.
<!--#endif-->
<!--#if (api)-->
The end-to-end suite starts a
whole stack of its own on the same ports, so stop yours first with `docker compose down`.
<!--#endif-->
<!--#if (anyWeb)-->
It then drives each web app in Chromium, which [testing](../guides/testing.md#the-browser-smoke)
shows how to install.
<!--#endif-->
[Testing](../guides/testing.md) says what each suite proves.
<!--#if (canAddFeature && api)-->

## Add a feature

Generate the skeleton of a feature owned by its caller:

```bash
dotnet run Tools/Tasks.cs new-feature Widgets Widget --access owner --deletion delete
```

It writes the files, a manifest under `.feature-manifest`, and the feature's page in
`docs/features/`; it makes the edits that compose the feature and adds its rows to
`docs/reference/api.md`; then it lists the page in the documentation index, builds, adds the
`AddWidgets` migration for the new tables and rewrites the OpenAPI reference. Rebuild the stack and
run the tests:

```bash
docker compose up -d --build --wait
dotnet run Tools/Tasks.cs test --no-integration
```

`/api/v1/widgets` now answers in Scalar. [Adding a feature](../guides/adding-a-feature.md) says what to
write next.
<!--#endif-->
<!--#if (canAddFeature && !api)-->

## Add a feature

Generate the skeleton of a feature owned by its caller:

```bash
dotnet run Tools/Tasks.cs new-feature Widgets Widget --access owner --deletion delete
```

It writes every layer but an HTTP one, which this project does not hold, a manifest under
`.feature-manifest`, and the feature's page in `docs/features/`; it makes the edits that compose the
feature, each host's `Program.cs` among them; then it lists the page in the documentation index,
builds and adds the `AddWidgets` migration for the new tables. Apply it as the first run did, then
run the tests:

```bash
dotnet ef database update --project Src/Infrastructure/AT.Infrastructure.Persistence --startup-project Src/Infrastructure/AT.Infrastructure.Persistence
dotnet run Tools/Tasks.cs test --no-integration
```

[Adding a feature](../guides/adding-a-feature.md) says what to write next, what each host shows of
the feature among it.
<!--#endif-->

## Add a part

<!--#if (!anyWeb)-->
To add a web app or a presentation project later, run `dotnet cleanarch add web-app` or
`dotnet cleanarch add presentation` at the project's root.
<!--#endif-->
<!--#if (anyWeb)-->
To add a web app, a shared web package or a presentation project later, run
`dotnet cleanarch add web-app`, `dotnet cleanarch add web-package` or
`dotnet cleanarch add presentation` at the project's root.
<!--#endif-->
[The overview](overview.md#how-this-project-was-generated) says what each part is.

## Next

- [Overview](overview.md): what this project was generated with.
- [Architecture](../architecture/README.md): the rules the code follows.
- [Build and CI](../guides/build-and-ci.md): the gate a change passes.
