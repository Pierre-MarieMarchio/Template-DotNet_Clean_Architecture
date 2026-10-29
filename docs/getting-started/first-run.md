# First run

<!--#if (canAddFeature)-->
From a fresh clone to a running stack, an authenticated call and a first feature of your own.
<!--#endif-->
<!--#if (!canAddFeature)-->
From a fresh clone to a running stack and a first call to the API.
<!--#endif-->
Run every command from the repository root.

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| .NET SDK | 10.0.100 or any later .NET 10 SDK, as `global.json` allows | building, running, every task |
| Docker with Compose v2 | any recent | the local stack and the integration tests |
<!--#if (anyWeb)-->
| Node.js | the version in `Src/Web/.node-version` | the web workspace |
| pnpm | the version `Src/Web/package.json` pins, run by Corepack | the web workspace |
<!--#endif-->

```bash
dotnet --version
```

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
It first creates the presentation project's pages with `dotnet new blazor` and adds the project to
the solution ([Presentation project](../features/presentation.md#creating-the-project)).
<!--#endif-->
<!--#if (presentationAvalonia)-->
It first creates the presentation project's views with `dotnet new avalonia.mvvm` and adds the
project to the solution ([Presentation project](../features/presentation.md#creating-the-project)),
and last writes the .NET client from the OpenAPI reference.
<!--#endif-->
<!--#if (presentationWpf)-->
It first adds the presentation project, its `.Core` project and their tests to the solution
([Presentation project](../features/presentation.md#creating-the-project)), and last writes the
.NET client from the OpenAPI reference.
<!--#endif-->
<!--#if (jobsWorker)-->
It also adds the worker's project in `Src/Presentation`, written in the shape of `dotnet new worker`,
to the solution with its tests.
<!--#endif-->
It formats the solution, writes the OpenAPI reference
the contract test reads ([HTTP API](../architecture/http-api.md#versioning-and-the-openapi-document)),
restores the local tools (`dotnet-ef` among them), then creates each database context's initial
migration under its project's `Migrations` folder. The project ships no migration: the first one is
written for this project's own model, and it is the project's from the commit on
([migrations](../guides/migrations.md#the-initial-migration)).
<!--#if (webFramework)-->
It then writes the .NET client where the web app calls the API through it, and creates the app as the
scaffold file in its folder describes, with the generator that file names.
<!--#endif-->
<!--#if (anyWeb)-->
It last installs the web workspace and generates its TypeScript API client, which needs Node.js and
pnpm.
<!--#endif-->

Both steps are required. Using directives sort around the project's own name, so their order is only
right once the project has its name: until bootstrap runs, `dotnet format --verify-no-changes` fails.
Without a migration, the API cannot create its schema, the migration drift tests fail, and `verify`
refuses to start.
<!--#if (ci)-->
It is also the first step of CI, so a first push would fail.
<!--#endif-->

## Run with Compose

```bash
cp .env.example .env
docker compose up -d --build --wait
```

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
| Mailpit, where mails land | <http://localhost:8025> |
<!--#if (postgreSql)-->
| PostgreSQL | `127.0.0.1:5432` |
<!--#endif-->
<!--#if (sqlServer)-->
| SQL Server | `127.0.0.1:1433` |
<!--#endif-->
<!--#if (files)-->
| Object store (SeaweedFS, S3 API) | <http://localhost:9000> |
<!--#endif-->
<!--#if (webFramework)-->
| Web app | <http://localhost:5173> |
<!--#endif-->
<!--#if (webPlaceholder)-->
| Web app | <http://localhost:8081> |
<!--#endif-->

Every port binds to `127.0.0.1`. The API speaks plain HTTP on 8080: TLS terminates upstream, as
[deployment](../guides/deployment.md#behind-a-proxy) describes.
<!--#if (sqlServer)-->
SQL Server runs capped at 1 GB of memory (`MSSQL_MEMORY_LIMIT_MB`); where a local SQL Server or the
host already holds port 1433, set `MSSQL_HOST_PORT` in `.env`.
<!--#endif-->
<!--#if (anyWeb)-->
The web app relays `/api` to the API, so a browser talks to one origin.
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

## Call the API

<!--#if (localAuth)-->
Register, confirm the address, sign in, and read your own profile. The sign-in reads the token with
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
the audience `Authentication:External:Audience`. Without a token every route answers `401`; with one,
the provider's subject becomes an internal owner on its first request.
<!--#endif-->
<!--#if (externalAuth && notes)-->

```bash
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/notes?page=1&pageSize=20"
```
<!--#endif-->
<!--#if (publicApi)-->
This API authenticates nobody, and every route is public. The readiness probe is a first call:

```bash
curl -s http://localhost:8080/health/ready
```
<!--#endif-->

## Explore with Scalar

<http://localhost:8080/scalar/v1> lists every route with its schema and lets you send requests. It and
the document behind it, `/openapi/v1.json`, exist in Development only.
<!--#if (accounts)-->
Paste an access token into its authentication field to call the protected routes.
<!--#endif-->

## Run the tests

```bash
dotnet run Tools/Tasks.cs test                    # the solution's suites, Docker needed
dotnet run Tools/Tasks.cs test --no-integration   # without Docker
dotnet run Tools/Tasks.cs end-to-end              # the end-to-end suite, against a stack of its own
```

<!--#if (postgreSql)-->
The integration suites start their own PostgreSQL in a container.
<!--#endif-->
<!--#if (sqlServer)-->
The integration suites start their own SQL Server in a container.
<!--#endif-->
<!--#if (sqlite)-->
The integration suites create their own SQLite file.
<!--#endif-->
The end-to-end suite starts a
whole stack of its own on the same ports, so stop yours first with `docker compose down`.
[Testing](../guides/testing.md) says what each suite proves.

<!--#if (canAddFeature)-->
## Add a feature

Generate the skeleton of a feature owned by its caller:

```bash
dotnet run Tools/Tasks.cs new-feature Widgets Widget --access owner --deletion delete
```

It writes the files, a manifest under `.feature-manifest`, and the feature's page in
`docs/features/`, then prints the edits it leaves to you. Make them in order: the solution compiles
again after the first, and the feature composes after the last. Add the row it prints to
`docs/README.md`.

The database the stack started has applied the initial migration, so add one for the new tables, then
rebuild and run the tests:

```bash
dotnet run Tools/Tasks.cs migration-add AddWidgets
docker compose up -d --build --wait
dotnet run Tools/Tasks.cs test --no-integration
```

`/api/v1/widgets` now answers in Scalar. [Adding a feature](../guides/adding-a-feature.md) says what to
write next.
<!--#endif-->

## Next

- [Overview](overview.md): what this project was generated with.
- [Architecture](../architecture/README.md): the rules the code follows.
- [Build and CI](../guides/build-and-ci.md): the gate a change passes.
