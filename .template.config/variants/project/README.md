# AppTemplate

## What it is

Replace this paragraph with what your product does, for whom, and what it is not.

This project is a .NET 10 HTTP API on Clean Architecture, with PostgreSQL through EF Core, a problem
document on every failure, and settings validated at start.
<!--#if (localAuth)-->
Callers sign in with local accounts: rotating refresh tokens, two-factor and passkeys.
<!--#endif-->
<!--#if (externalAuth)-->
Callers bring a token their OpenID Connect provider issued.
<!--#endif-->
<!--#if (publicApi)-->
Every route is public: the API authenticates nobody.
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
<!--#if (webReact)-->
| Web app | <http://localhost:5173> |
<!--#endif-->
<!--#if (webAngular)-->
| Web app | <http://localhost:4200> |
<!--#endif-->
<!--#if (webPlaceholder)-->
| Web app | <http://localhost:8081> |
<!--#endif-->

[First run](docs/getting-started/first-run.md) continues from there: running on the host, a first call
to the API, the tests and a first feature.

## Documentation

- [The documentation index](docs/README.md): every page, by what you are doing.
- [AGENTS.md](AGENTS.md): where the truths live, and the rules for changing code and docs.
- [CONTRIBUTING.md](CONTRIBUTING.md): what has to be true before a change is done.
- [SECURITY.md](SECURITY.md): what this project protects, and what a deployment must still do.

## Licence

This project ships without a licence. Add a licence file at the root before publishing it.
