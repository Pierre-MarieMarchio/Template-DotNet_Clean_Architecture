# AppTemplate

## What it is

Replace this paragraph with what your product does, for whom, and what it is not.

Replace this line with the links your team owns: the build status, the environments, the issue tracker.

<!--#if (api && postgreSql)-->
This project is a .NET 10 HTTP API on Clean Architecture, with PostgreSQL through EF Core, a problem
document on every failure, and settings validated at start.
<!--#endif-->
<!--#if (api && sqlServer)-->
This project is a .NET 10 HTTP API on Clean Architecture, with SQL Server through EF Core, a problem
document on every failure, and settings validated at start.
<!--#endif-->
<!--#if (api && sqlite)-->
This project is a .NET 10 HTTP API on Clean Architecture, with SQLite through EF Core, a problem
document on every failure, and settings validated at start. SQLite is a development database: the API
refuses to start outside Development.
<!--#endif-->
<!--#if (!api && postgreSql)-->
This project is a set of .NET 10 hosts on Clean Architecture, with no HTTP API, with PostgreSQL
through EF Core and settings validated at start.
<!--#endif-->
<!--#if (!api && sqlServer)-->
This project is a set of .NET 10 hosts on Clean Architecture, with no HTTP API, with SQL Server
through EF Core and settings validated at start.
<!--#endif-->
<!--#if (!api && sqlite)-->
This project is a set of .NET 10 hosts on Clean Architecture, with no HTTP API, with SQLite through
EF Core and settings validated at start. SQLite is a development database: this project is not
meant to be deployed.
<!--#endif-->
<!--#if (localAuth && api)-->
Callers sign in with local accounts: rotating refresh tokens, two-factor and passkeys.
<!--#endif-->
<!--#if (localAuth && !api)-->
Users sign in with local accounts: rotating refresh tokens, two-factor and passkeys.
<!--#endif-->
<!--#if (externalAuth && api)-->
Callers bring a token their OpenID Connect provider issued.
<!--#endif-->
<!--#if (externalAuth && !api)-->
Users sign in with an OpenID Connect provider.
<!--#endif-->
<!--#if (publicApi && api)-->
Every route is public: the API authenticates nobody.
<!--#endif-->
<!--#if (publicApi && !api)-->
The hosts authenticate nobody.
<!--#endif-->
This project's code lives in four layers under `Src/`, mirrored under `Tests/`;
[project layout](docs/reference/project-layout.md) says where a file goes.
<!--#if (anyWeb)-->
The web apps live in the pnpm workspace `Src/Web`.
<!--#endif-->
[The overview](docs/getting-started/overview.md) lists what it contains and the options it was
generated with.

## Run it

You need the .NET SDK `global.json` pins and Docker with Compose v2.
<!--#if (anyWeb)-->
The web workspace also needs Node.js and pnpm (`corepack enable`).
<!--#endif-->

```bash
dotnet run Tools/Tasks.cs bootstrap     # once, right after generating, online; commit what it writes
cp .env.example .env
docker compose up -d --build --wait
```

<!--#if (externalAuth && api)-->
Set `EXTERNAL_AUTH_AUTHORITY` and `EXTERNAL_AUTH_AUDIENCE` in `.env` before starting: the API refuses
to start without its identity provider.

<!--#endif-->
<!--#if (externalAuth && !api)-->
Set `EXTERNAL_AUTH_AUTHORITY` and `EXTERNAL_AUTH_AUDIENCE` in `.env` before starting: the hosts refuse
to start without their identity provider.

<!--#endif-->
| Service | Address |
|---|---|
<!--#if (api)-->
| API | <http://localhost:8080> |
| Scalar, the OpenAPI UI | <http://localhost:8080/scalar/v1> |
<!--#endif-->
<!--#if (presentationBlazorServer)-->
| The portal | <http://localhost:8090> |
<!--#endif-->
<!--#if (presentationMcp)-->
| The MCP server | <http://localhost:8091/mcp> |
<!--#endif-->
| Mailpit | <http://localhost:8025> |
<!--#if (webFramework)-->
| Web app | <http://localhost:5173> |
<!--#endif-->
<!--#if (webPlaceholder)-->
| Web app | <http://localhost:8081> |
<!--#endif-->

<!--#if (api && canAddFeature)-->
[First run](docs/getting-started/first-run.md) continues from there: running on the host, a first call
to the API, the tests and a first feature.
<!--#endif-->
<!--#if (api && !canAddFeature)-->
[First run](docs/getting-started/first-run.md) continues from there: running on the host, a first call
to the API and the tests.
<!--#endif-->
<!--#if (!api && canAddFeature)-->
[First run](docs/getting-started/first-run.md) continues from there: applying the migrations, the
tests and a first feature.
<!--#endif-->
<!--#if (!api && !canAddFeature)-->
[First run](docs/getting-started/first-run.md) continues from there: applying the migrations and the
tests.
<!--#endif-->

## Everyday commands

```bash
dotnet run Tools/Tasks.cs test --no-integration   # the tests that need no Docker
dotnet run Tools/Tasks.cs verify                  # the whole gate, Docker needed
dotnet run Tools/Tasks.cs help                    # every task and its options
dotnet run Tools/Tasks.cs help verify             # one task alone
docker compose down                               # stop the stack
```

## Documentation

- [The documentation index](docs/README.md): every page, by what you are doing.
<!--#if (agentFiles)-->
- [AGENTS.md](AGENTS.md): how to work in this repository, for people and coding agents alike: the
  commands, where code goes, what done means.
<!--#endif-->
- [CONTRIBUTING.md](CONTRIBUTING.md): the pull request rules, and where each rule of this project lives.
- [SECURITY.md](SECURITY.md): what this project protects, and what a deployment must still do.

## Licence

This project ships without a licence. Add a licence file at the root before publishing it.
