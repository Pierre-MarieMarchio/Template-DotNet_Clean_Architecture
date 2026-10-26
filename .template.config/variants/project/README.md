# AppTemplate

## What it is

Replace this paragraph with what your product does, for whom, and what it is not.

Replace this line with the links your team owns: the build status, the environments, the issue tracker.

<!--#if (postgreSql)-->
This project is a .NET 10 HTTP API on Clean Architecture, with PostgreSQL through EF Core, a problem
document on every failure, and settings validated at start.
<!--#endif-->
<!--#if (sqlServer)-->
This project is a .NET 10 HTTP API on Clean Architecture, with SQL Server through EF Core, a problem
document on every failure, and settings validated at start.
<!--#endif-->
<!--#if (sqlite)-->
This project is a .NET 10 HTTP API on Clean Architecture, with SQLite through EF Core, a problem
document on every failure, and settings validated at start. SQLite is a development database: the API
refuses to start outside Development.
<!--#endif-->
<!--#if (localAuth)-->
Callers sign in with local accounts: rotating refresh tokens, two-factor and passkeys.
<!--#endif-->
<!--#if (externalAuth)-->
Callers bring a token their OpenID Connect provider issued.
<!--#endif-->
<!--#if (publicApi)-->
Every route is public: the API authenticates nobody.
<!--#endif-->
The API's code lives in four layers under `Src/`, mirrored under `Tests/`;
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
dotnet run Tools/Tasks.cs bootstrap     # once, after a fresh clone; commit what it changes
cp .env.example .env
docker compose up -d --build --wait
```

<!--#if (externalAuth)-->
Set `EXTERNAL_AUTH_AUTHORITY` and `EXTERNAL_AUTH_AUDIENCE` in `.env` before starting: the API refuses
to start without its identity provider.

<!--#endif-->
| Service | Address |
|---|---|
| API | <http://localhost:8080> |
| Scalar, the OpenAPI UI | <http://localhost:8080/scalar/v1> |
| Mailpit | <http://localhost:8025> |
<!--#if (webFramework)-->
| Web app | <http://localhost:5173> |
<!--#endif-->
<!--#if (webPlaceholder)-->
| Web app | <http://localhost:8081> |
<!--#endif-->

[First run](docs/getting-started/first-run.md) continues from there: running on the host, a first call
to the API, the tests and a first feature.

## Everyday commands

```bash
dotnet run Tools/Tasks.cs test --no-integration   # the tests that need no Docker
dotnet run Tools/Tasks.cs verify                  # the whole gate, Docker needed
dotnet run Tools/Tasks.cs help                    # every task and its options
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
