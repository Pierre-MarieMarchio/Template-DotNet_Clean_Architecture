# Getting started

Two paths. Compose brings the whole stack up and installs nothing on the host beyond Docker;
running on the host gives you a debugger and needs a PostgreSQL and an SMTP sink from somewhere.
<!--#if (localAuth)-->
Both end at the same place: an account you registered, confirmed and signed in with.
<!--#endif-->

- [With Compose](#with-compose)
- [On the host](#on-the-host)
<!--#if (localAuth)-->
- [Register to first request](#register-to-first-request)
<!--#endif-->
- [Pointing it at your own database](#pointing-it-at-your-own-database)
- [Ports and TLS](#ports-and-tls)
- [The task launcher](#the-task-launcher)

## With Compose

Brings up PostgreSQL, a mail catcher,
<!--#if (files)-->
an S3-compatible object store and
<!--#endif-->
the API, which serves HTTP and runs the periodic jobs.

```bash
cp .env.example .env          # working localhost defaults; edit if you like
docker compose up --build
```

| | Address |
|---|---|
| API | <http://localhost:8080> |
| Liveness | <http://localhost:8080/health> |
| Readiness | <http://localhost:8080/health/ready> |
| OpenAPI document — development only | <http://localhost:8080/openapi/v1.json> |
| OpenAPI UI — [Scalar](https://scalar.com), development only | <http://localhost:8080/scalar/v1> |
| Mailpit — mails land here | <http://localhost:8025> |
| PostgreSQL | `127.0.0.1:5432` |
| Mailpit SMTP | `127.0.0.1:1025` |
<!--#if (files)-->
| Object store (SeaweedFS) S3 API — uploaded files land here; no console, browse with any S3 client | `127.0.0.1:9000` |
<!--#endif-->

```bash
docker compose logs -f api      # follow the API
docker compose down             # stop, keep the database volume
docker compose down -v          # stop and delete the database
```

Every published port is bound to `127.0.0.1`, so nothing is reachable from your local network.
**TLS is not terminated in the container** — it speaks plain HTTP on 8080 and nothing else, and
`UseHttpsRedirection` is deliberately absent from the pipeline because it would 307 the
orchestrator's health probe. A deployment must terminate TLS and enforce HTTPS upstream; see
[`../SECURITY.md`](../SECURITY.md).

`.env` is git-ignored. `.env.example` is tracked, contains no real secret, and every variable it
defines is documented in [`CONFIGURATION.md`](CONFIGURATION.md).

> The two OpenAPI endpoints are mapped with `.AllowAnonymous()` inside the `IsDevelopment()`
> branch, so they are reachable without a token in development and do not exist at all outside it.
<!--#if (accounts)-->
> They need that opt-out because the default-deny fallback policy would otherwise catch them,
> exactly as it would any other endpoint — see
> [`API-CONVENTIONS.md`](API-CONVENTIONS.md#authorisation-is-default-deny).
<!--#endif-->

## On the host

You supply a PostgreSQL instance and an SMTP sink. The easiest is the pair from Compose:

<!--#if (files)-->
```bash
cp .env.example .env
docker compose up -d db mailpit objectstore
```
<!--#else
```bash
cp .env.example .env
docker compose up -d db mailpit
```
<!--#endif-->

`Src/Presentation/AppTemplate.Api/appsettings.Development.json` already points at `localhost:5432`
and `localhost:1025` with the same credentials as `.env.example`, so this needs no further
configuration:

```bash
dotnet restore AppTemplate.sln
dotnet run --project Src/Presentation/AppTemplate.Api --launch-profile http
```

The API listens on <http://localhost:5187>. The `https` profile adds <https://localhost:7004>
against the development certificate:

```bash
dotnet dev-certs https --trust
dotnet run --project Src/Presentation/AppTemplate.Api --launch-profile https
```

**In Development — and only in Development — startup applies pending migrations**
<!--#if (localAuth)-->
for both
contexts before serving traffic, so the schema is created for you.
<!--#else
before serving traffic, so the schema is created for you.
<!--#endif--> Any other environment starts
without touching the schema; [`MIGRATIONS.md`](MIGRATIONS.md) says how a deployment applies them
instead.

## Register to first request

<!--#if (localAuth)-->
The same walkthrough, executable and with the variables already wired, is
[`AppTemplate.Api.http`](../AppTemplate.Api.http) — register, read the confirmation mail out of
mailpit, confirm, sign in, create a list, add and complete an item, rotate the token pair, log out.
It works in the VS Code REST Client and in Visual Studio's `.http` editor.

By hand. The API answers on **5187** when you run it on the host (`dotnet run`, above) and on
**8080** when it runs in the Compose stack; the commands read their address from `API`, so set it to
the one you started. The sign-in command uses `jq` to read the token out of the response; without
it, run that `curl` alone and copy `tokens.accessToken` into `TOKEN` by hand.

```bash
API=http://localhost:5187     # host run; http://localhost:8080 under Compose

# 1. create an account (200; confirmationEmailSent tells you whether the mail went out)
curl -s -X POST "$API/api/v1/auth/register" \
  -H 'Content-Type: application/json' \
  -d '{"userName":"alice","email":"alice@example.com","password":"Passw0rd!x"}'

# 2. open http://localhost:8025, click the confirmation mail, and POST what it carries
curl -s -X POST "$API/api/v1/auth/confirm-email" \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","token":"<token from the link fragment, URL-decoded>"}'  # 204

# 3. sign in — the body is tagged {"status":"authenticated","tokens":{…}}
TOKEN=$(curl -s -X POST "$API/api/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","password":"Passw0rd!x"}' | jq -r .tokens.accessToken)
```

<!--#if (todoLists)-->
```bash
# 4. use the API
curl -s -X POST "$API/api/v1/todo-lists" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"Groceries"}'                                                        # 201 + Location

curl -s -H "Authorization: Bearer $TOKEN" \
  "$API/api/v1/todo-lists?page=1&pageSize=20"
```
<!--#endif-->
<!--#if (todoLists || !notes)-->
<!--#else
```bash
# 4. use the API
curl -s -X POST "$API/api/v1/notes" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"title":"Groceries","body":"Milk, eggs"}'                                   # 201 + Location

curl -s -H "Authorization: Bearer $TOKEN" \
  "$API/api/v1/notes?page=1&pageSize=20"
```
#endif-->
<!--#if (todoLists || notes)-->
<!--#else
```bash
# 4. use the API: the caller's own profile, the one route every project has
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/v1/auth/me"
```
#endif-->

The mail carries the token percent-encoded in the link's fragment (`+` is `%2B`, `/` is `%2F`,
`=` is `%3D`), and the API expects it decoded. Posted as it appears, it answers `400`
`auth.confirmEmail.invalid`, which reads as an expired link and is not one. Decode it first:

```bash
python3 -c 'import sys, urllib.parse as u; print(u.unquote(sys.argv[1]))' '<token from the link>'
```

Skip step 2 and the sign-in fails with `auth.login.invalidCredentials`:
`Identity:RequireConfirmedEmail` is `true`, and an unconfirmed account is deliberately
indistinguishable from a wrong password.

Add `-H 'Accept-Language: fr'` to step 1 and the confirmation mail arrives in French. The subject
comes from the template's own `<title>`, so a subject and a body can never end up in different
languages — [`API-CONVENTIONS.md`](API-CONVENTIONS.md#mail-is-written-in-the-readers-language).
<!--#elseif (externalAuth)
Every request to this API carries a bearer token your identity provider issued; there is no local
account to register, and no sign-in route on this API. Obtain a token from the provider with the
audience `Authentication:External:Audience`, and use it as the `Authorization: Bearer` header.
<!--#else
This API authenticates nobody: every route is public, and a first request needs no token at all.
<!--#endif-->

## Pointing it at your own database

<!--#if (localAuth)-->
Two values are all you have to supply: `ConnectionStrings:Default` and `Jwt:Key`. They go in
`dotnet user-secrets` — `AppTemplate.Api.csproj` already carries a `UserSecretsId`, so the store
exists and overrides both appsettings files.

```bash
cd Src/Presentation/AppTemplate.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=…;Database=…;Username=…;Password=…"
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
```
<!--#elseif (externalAuth)
Two values are all you have to supply: `ConnectionStrings:Default` and the provider's
`Authentication:External:Authority` with `Authentication:External:Audience`. They go in
`dotnet user-secrets` — `AppTemplate.Api.csproj` already carries a `UserSecretsId`, so the store
exists and overrides both appsettings files.

```bash
cd Src/Presentation/AppTemplate.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=…;Database=…;Username=…;Password=…"
dotnet user-secrets set "Authentication:External:Authority" "https://your-provider.example.com"
dotnet user-secrets set "Authentication:External:Audience" "this-api"
```
<!--#else
One value is all you have to supply: `ConnectionStrings:Default`. It goes in
`dotnet user-secrets` — `AppTemplate.Api.csproj` already carries a `UserSecretsId`, so the store
exists and overrides both appsettings files.

```bash
cd Src/Presentation/AppTemplate.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=…;Database=…;Username=…;Password=…"
```
<!--#endif-->

**Never put a secret in `appsettings.json` or `appsettings.Development.json` — both are tracked in
git.** A deployed environment supplies these, and every other blank in `appsettings.json`, as
environment variables instead, with `__` as the section separator: `ConnectionStrings__Default`,
`Jwt__Key`, `Cors__AllowedOrigins__0`. The full key list and what each value is validated against
is [`CONFIGURATION.md`](CONFIGURATION.md#secrets-use-dotnet-user-secrets).

<!--#if (localAuth)-->
> **Development seeding creates an administrator, and it is off by default.** `IdentitySeed:Enabled`
> is `false`, the seeder throws if it is turned on outside Development, and
> `IdentitySeed:AdminPassword` has no default — so enabling seeding without one fails startup
> instead of creating a guessable admin. Never enable it anywhere reachable from the internet:
> [`CONFIGURATION.md`](CONFIGURATION.md#identityseed--development-only).
<!--#endif-->

### What a wrong setting does

Configuration is layered `appsettings.json` → `appsettings.Development.json` → user secrets →
environment variables, and every section binds to an options class whose validator runs
with `.ValidateOnStart()`. **A missing or out-of-range value fails the host at startup, in one
pass, with a message naming the exact key** — not on the first request that needed it.
<!--#if (localAuth)-->
Blanking
`EmailConfirmation:ConfirmEmailUrl` and shortening `Jwt:Key` produces
`'Jwt:Key' must be at least 32 bytes long to sign HS256 tokens.` and
`'EmailConfirmation:ConfirmEmailUrl' is required.` before Kestrel binds a port.
<!--#else
An out-of-range value produces a message naming the exact key before Kestrel binds a port.
<!--#endif-->

`appsettings.json` is tracked and holds no secrets — every secret-shaped value in it is an empty
string, which is why that file alone will not boot the app.

One consequence to know before pointing this at a real mail relay: **startup rejects every SMTP
mode that can end up sending in the clear against a non-loopback host**, `Auto` included, unless
`Email:AllowInsecureTransport` says so outright. A containerised sink such as mailpit needs both
switches, which `docker-compose.yml` and `.env.example` already set —
[`CONFIGURATION.md`](CONFIGURATION.md#email) has the three valid shapes.

## Ports and TLS

| Context | HTTP | HTTPS |
|---|---|---|
| `dotnet run` — `http` profile | 5187 | — |
| `dotnet run` — `https` profile | 5187 | 7004 (development certificate) |
| Container / Compose | 8080 | **none** |

**The container speaks plain HTTP on 8080 and nothing else.** TLS is terminated upstream — by an
ingress controller, reverse proxy or cloud load balancer — and HTTPS redirection is deliberately
absent from the pipeline: the container listens on plain 8080, so `UseHttpsRedirection` would 307
the orchestrator's health probe and every internal call. Enforce HTTPS at the ingress instead, which
is also where HSTS belongs (see [`../SECURITY.md`](../SECURITY.md)).

An `EXPOSE 8081` with no certificate provisioned, no `ASPNETCORE_HTTPS_PORTS` set and nothing bound
to the port is a published port that cannot answer. Half-configured TLS is worse than none, because
it reads as present. If you do need TLS inside the container, mount a certificate and set
`ASPNETCORE_HTTPS_PORTS` with `Kestrel__Certificates__Default__*` explicitly — do not just add an
`EXPOSE`.

## The task launcher

`Tools/Tasks.cs` wraps the real `dotnet` and `docker` commands and prints each one before running
it, so the script is also the documentation — copy the printed line and you get the same result
without it. No task hides a flag that changes the meaning of a build.

```bash
dotnet run Tools/Tasks.cs compose-up             # the whole stack, waiting until each is healthy
dotnet run Tools/Tasks.cs run                    # the API on the host
dotnet run Tools/Tasks.cs test                   # everything
dotnet run Tools/Tasks.cs test --no-integration  # skips the Testcontainers suite
dotnet run Tools/Tasks.cs new-feature Widgets Widget --access shared   # the skeleton of one feature
dotnet run Tools/Tasks.cs remove-feature Widgets           # what removing it involves; --apply undoes an untouched skeleton
dotnet run Tools/Tasks.cs verify                 # the whole gate, in CI's order
```

The first run of a given file compiles it — a second or two on a warm machine — and every run
after that is served from the build cache. A first invocation that pauses is the compiler, not a
hang. [`BUILD-AND-CI.md`](BUILD-AND-CI.md) lists every task and what each gate checks.
