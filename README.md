# Clean Architecture .NET Template

A production-shaped starting point for a .NET 10 HTTP API: Clean Architecture
layering, PostgreSQL via EF Core, ASP.NET Identity with JWT access tokens and
opaque rotating refresh tokens, default-deny authorisation, RFC 7807 errors, and
configuration that fails fast when it is wrong.

The sample domain is a to-do list — enough to show an aggregate with real
invariants, domain events, a read/write port split and a `Result`-based error
policy, without becoming an application you have to delete.

> - Layer boundaries and the reasoning behind them: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
> - Every configuration key: [docs/CONFIGURATION.md](docs/CONFIGURATION.md)
> - Why each decision was made, one file per decision: [docs/adr/](docs/adr/README.md)

## What is in the box

- **.NET 10** (`net10.0`), SDK pinned by `global.json`
- **PostgreSQL** through `Npgsql.EntityFrameworkCore.PostgreSQL`, one connection
  string, one `DbContext`, one migrations history, two schemas
- **ASP.NET Identity** + JWT bearer, opaque refresh tokens that rotate on every
  use, email confirmation by POST, logout that actually revokes
- **Default-deny authorisation** — a fallback policy requires an authenticated
  user, so an endpoint is protected unless it opts out
- **RFC 7807 ProblemDetails** on every failure — including 401 and 403 — with a
  stable machine-readable `code` and a `traceId`
- **EF Core maps persistence models, not the domain entities.** The aggregate has no
  EF concepts in it; a mapper converts, and a reflection-driven fidelity test fails
  when a property does not survive the round trip
- **Rate limiting** — 10 requests/minute per IP on the auth endpoints, 300/minute
  globally
- **Health endpoints** — `/health` (liveness) and `/health/ready` (readiness)
- **OpenAPI** via the built-in `Microsoft.AspNetCore.OpenApi`, with
  [Scalar](https://scalar.com) as the UI in development (no Swashbuckle)
- **Central Package Management** — every version pinned once in
  `Directory.Packages.props`
- **Warnings as errors** and `.editorconfig` enforced in the build and in CI
- **Options validated at startup** — a bad setting fails the host, not the first
  request
- **CI** — build, format check, tests with coverage, vulnerable-package audit,
  Docker image build, compose manifest check

## Prerequisites

| | Needed for |
|---|---|
| [.NET SDK 10.0.300+](https://dotnet.microsoft.com/download) | building and running |
| [Docker](https://docs.docker.com/get-docker/) with Compose v2 | the container path, and integration tests (Testcontainers) |
| PostgreSQL 17+ | only if you run the API on the host without Docker |
| `git` | |

```bash
dotnet --version     # expect 10.0.300 or a later 10.0.3xx
```

`global.json` pins the feature band with `rollForward: latestFeature`, so a later
`10.0.3xx` patch is fine and an `8.x`/`9.x` SDK is refused outright.

## Using this as a `dotnet new` template

This repository is itself a `dotnet new` template. Install it from a clone or from
the repository path directly, then generate a project under your own name:

```bash
dotnet new install <path-to-this-repository>
dotnet new cleanarch-webapi -n Acme.OrderManagement
cd Acme.OrderManagement

./tasks.ps1 bootstrap        # required once — see the first note below
```

Generate **outside** the template repository. `dotnet new` reads the template from the
path you installed, so generating into a subdirectory of that path makes the engine
copy its own partial output and you get a nested duplicate.

`AppTemplate` is the token every project name, namespace, `.sln`/`.csproj` file name
and Docker/Compose identifier is derived from — `-n Acme.OrderManagement` yields
`Acme.OrderManagement.Domain`, `Acme.OrderManagement.Api`, an Acme.OrderManagement.sln
solution file, a root namespace of `Acme.OrderManagement.*`, and a Compose project /
image name of `acme-ordermanagement`. Every project GUID is regenerated, so two
generated projects never collide if opened side by side.

A few things worth knowing before you commit the result:

- **`./tasks.ps1 bootstrap` is not optional, and it is the first thing to run.** Sorted
  `using` directives are ordered alphabetically, so where a project's own namespace falls
  relative to `FluentValidation`, `Microsoft.*` and the rest depends on the name you chose.
  `AppTemplate` sorts one way, `Acme.OrderManagement` another. Until you run it once,
  `dotnet format --verify-no-changes` fails — and since that is the *first* step of the
  CI workflow you inherit, your first push fails with it. One run fixes it permanently.
  It cannot be fixed in the template itself: no single committed ordering is correct for
  every possible name.
- **The `TodoLists` feature ships by default** as the worked example for
  [docs/ADDING-A-FEATURE.md](docs/ADDING-A-FEATURE.md); there is no generator
  switch to exclude it, and that doc's last section explains why, plus what to
  remove by hand if you want it gone.
- `dotnet new uninstall <path-to-this-repository>` removes the template again.

`.github/workflows/ci.yml`'s `template` job runs this exact install → generate →
build → test sequence, under a different name, on every push — a template that
generates a broken project is worse than a repository to clone, so that flow is a
CI gate, not just a manual step.

## Quick start — Docker

Brings up PostgreSQL, a mail catcher and the API. Nothing is installed on the host
beyond Docker.

```bash
git clone <your-fork-url> && cd Template-DotNet_Clean_Architecture

cp .env.example .env          # working localhost defaults; edit if you like
docker compose up --build
```

Then:

| | URL |
|---|---|
| API | <http://localhost:8080> |
| Liveness | <http://localhost:8080/health> |
| Readiness | <http://localhost:8080/health/ready> |
| OpenAPI document (dev only) | <http://localhost:8080/openapi/v1.json> |
| OpenAPI UI — Scalar (dev only) | <http://localhost:8080/scalar/v1> |
| Mailpit — confirmation emails land here | <http://localhost:8025> |
| PostgreSQL | `127.0.0.1:5432` |
| Mailpit SMTP | `127.0.0.1:1025` |

> The two OpenAPI endpoints are mapped with `.AllowAnonymous()`, inside the
> `IsDevelopment()` branch — so they are reachable without a token in development and do
> not exist at all outside it. They need the opt-out because the default-deny fallback
> policy would otherwise catch them, exactly as it would any other endpoint.

Every published port is bound to `127.0.0.1`, so nothing is reachable from your
local network. TLS is **not** terminated in the container — see
[Ports and TLS](#ports-and-tls).

`.env` is git-ignored. `.env.example` is tracked, contains no real secret, and every
variable it defines is documented in [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

```bash
docker compose logs -f api      # follow the API
docker compose down             # stop, keep the database volume
docker compose down -v          # stop and delete the database
```

## Quick start — run on the host

You supply a PostgreSQL instance and an SMTP sink; the easiest is the pair from
Compose:

```bash
cp .env.example .env
docker compose up -d db mailpit
```

`appsettings.Development.json` is already pointed at `localhost:5432` and
`localhost:1025` with the same credentials as `.env.example`, so this works with no
further configuration:

```bash
dotnet restore AppTemplate.sln
dotnet run --project Src/Presentation/AppTemplate.Api --launch-profile http
```

The API listens on <http://localhost:5187> (`https` profile: <https://localhost:7004>).
In Development — and **only** in Development — startup applies pending migrations
before serving traffic, so the schema is created for you.

### Clone to a working request, end to end

Verified against a running instance. `jq` is optional; it is only there to read the
token out of the response.

> The same walkthrough, executable and with variables already wired, is in
> [`AppTemplate.Api.http`](AppTemplate.Api.http) — register, read the confirmation mail out of mailpit, confirm, log
> in, create a list, add and complete an item, rotate the token pair, log out. It works in the VS
> Code REST Client and in Visual Studio's `.http` editor.

```bash
# 1. create an account (200; confirmationEmailSent tells you whether the mail went out)
curl -s -X POST http://localhost:5187/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"userName":"alice","email":"alice@example.com","password":"Passw0rd!x"}'

# 2. open http://localhost:8025, click the confirmation mail, and POST what it carries
curl -s -X POST http://localhost:5187/api/v1/auth/confirm-email \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","token":"<token from the link fragment>"}'      # 204

# 3. log in
TOKEN=$(curl -s -X POST http://localhost:5187/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","password":"Passw0rd!x"}' | jq -r .accessToken)

# 4. use the API
curl -s -X POST http://localhost:5187/api/v1/todo-lists \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"Groceries"}'                                                        # 201 + Location

curl -s -H "Authorization: Bearer $TOKEN" \
  'http://localhost:5187/api/v1/todo-lists?page=1&pageSize=20'
```

Without step 2 the login fails with `auth.login.invalidCredentials`:
`Identity:RequireConfirmedEmail` is `true`, and an unconfirmed account is
deliberately indistinguishable from a wrong password.

### Configuring it yourself

```bash
cd Src/Presentation/AppTemplate.Api

dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=<yours>"

# HS256 signing key — minimum 32 bytes, enforced at startup
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"

dotnet user-secrets list
```

`AppTemplate.Api.csproj` already carries a `UserSecretsId`, so the store exists. Secrets live
outside the repository and override both appsettings files. **Never put a secret in
`appsettings.json` or `appsettings.Development.json` — both are tracked in git.**

In a deployed environment, use environment variables instead. Nest sections with a
double underscore: `ConnectionStrings__Default`, `Jwt__Key`,
`Identity__PasswordRequiredLength`, `Cors__AllowedOrigins__0`.

### Development seeding is off by default

> **Warning.** `IdentitySeed` creates an **administrator** account with the `Admin`
> role and a pre-confirmed email address.
>
> - It is **disabled** unless `IdentitySeed:Enabled=true`.
> - `IdentitySeeder` **throws** if it is enabled outside the Development
>   environment, rather than quietly shipping a known superuser to production.
> - `IdentitySeed:AdminPassword` has **no default**. Enabling seeding without
>   explicitly configuring a password **fails startup validation** instead of
>   creating a guessable admin.
>
> Never enable it anywhere reachable from the internet.

To enable it locally:

```bash
cd Src/Presentation/AppTemplate.Api
dotnet user-secrets set "IdentitySeed:Enabled" "true"
dotnet user-secrets set "IdentitySeed:AdminEmail" "you@localhost"
dotnet user-secrets set "IdentitySeed:AdminPassword" "<a real password you choose>"
```

## The API surface

All routes are versioned. `api-supported-versions: 1.0` comes back on every
response, and the version segment substitutes into the route
(`api/v{version:apiVersion}/…`).

### Authentication — `api/v1/auth/*`

Every action is explicitly `[AllowAnonymous]` and rate-limited to **10 requests per
minute per client IP**.

| Method | Route | Success | Notes |
|---|---|---|---|
| POST | `/api/v1/auth/register` | 200 | Body carries `confirmationEmailSent`; the account is committed before the mail is sent, so a delivery failure is recoverable, not fatal. |
| POST | `/api/v1/auth/login` | 200 | Returns `accessToken`, `accessTokenExpiresAt`, `refreshToken`, `refreshTokenExpiresAt`. |
| POST | `/api/v1/auth/refresh` | 200 | Consumes the presented refresh token and returns a new pair. |
| POST | `/api/v1/auth/confirm-email` | 204 | **POST with a JSON body**, not a GET with a query string. |
| POST | `/api/v1/auth/resend-confirmation-email` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/logout` | 204 | Revokes the presented refresh token. Idempotent. |

**The refresh token is in the response body, not a cookie.** It is an opaque
32-byte CSPRNG value, base64url-encoded — not a JWT — and only its SHA-256 hash is
stored. Every presentation rotates it. Presenting one that was already rotated or
revoked is treated as theft: the **entire token family for that user is revoked** and
the request fails. Verified: replaying a consumed token returns 401, and the
token it had been rotated into stops working too.

### To-do lists — `api/v1/todo-lists/*`

Authentication required (no opt-out). One controller for one aggregate root; items
are addressed **through their list**, because that is what the aggregate boundary
means — there is no route that reaches an item without naming its list.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/todo-lists?page=1&pageSize=20&sort=createdAt:desc` | 200 — paged summaries of the caller's own lists |
| GET | `/api/v1/todo-lists/{todoListId}` | 200 — the list with its items and tags |
| POST | `/api/v1/todo-lists` | 201 + `Location` |
| PUT | `/api/v1/todo-lists/{todoListId}` | 204 — rename; the id comes from the route, never the body |
| DELETE | `/api/v1/todo-lists/{todoListId}` | 204 |
| POST | `/api/v1/todo-lists/{todoListId}/items` | 201 |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/complete` | 204 |
| DELETE | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 204 |

The old `api/ListTodos`, `api/TodoItem` and `api/TodoTag` controllers are gone.

### Maintenance — `api/v1/maintenance/*`

| Method | Route | Success |
|---|---|---|
| DELETE | `/api/v1/maintenance/idempotency-keys/expired` | 200 — the number of rows removed |

Requires the `Administrator` policy — an authenticated non-admin gets `403`. This is the
one endpoint whose authority is more than "authenticated plus ownership", and it exists
because the idempotency store grows until something prunes it. Schedule it.

**Conditional requests.** Every read of a single list or item publishes the list's
version as a strong, opaque `ETag`; every write of one honours `If-Match`, so a stale
edit — decided against a version somebody else has since changed — is refused with
`412` instead of silently overwriting it. `If-Match: *` asserts that the resource
exists, so a missing or someone-else's list also answers `412`, not `404`. Sending no
`If-Match` at all is accepted unless `Concurrency:IfMatch` is set to `Required`, in
which case it is refused with `428` — see `docs/adr/0013`. `AppTemplate.Api.http` walks through
the whole round trip.

### Collection queries — sorting, filtering, paging

The collection endpoint takes the same contract every collection endpoint in this
template should take. It is deliberately a **closed** contract: a caller may only ask
for what a feature has declared, and anything else is a `400` with a stable `code`
rather than a clamp, a guess or a 500.

| Parameter | Type | Default | Bound |
|---|---|---|---|
| `sort` | `field[:asc\|:desc]`, comma-separated | `createdAt:desc` | ≤ 3 terms, each a whitelisted field, no field twice |
| `search` | text, matched against the list **name** | none | ≤ 100 characters |
| `createdAfter` / `createdBefore` | ISO 8601 instant | none | `createdAfter` must not be later than `createdBefore` |
| `paging` | `offset` or `cursor` | `offset` | — |
| `page` | integer, 1-based | `1` | ≥ 1, offset mode only |
| `pageSize` | integer | `20` | 1…100 |
| `cursor` | opaque token from `nextCursor` | none | cursor mode only, ≤ 512 characters |

```bash
# newest first, then by name, second page of ten
GET /api/v1/todo-lists?sort=createdAt:desc,name:asc&page=2&pageSize=10

# name contains "grocer", case-insensitively, created this year
GET /api/v1/todo-lists?search=grocer&createdAfter=2026-01-01T00:00:00Z

# keyset paging: first page, then follow nextCursor
GET /api/v1/todo-lists?paging=cursor&pageSize=10&sort=name:asc
GET /api/v1/todo-lists?paging=cursor&pageSize=10&sort=name:asc&cursor=eyJmIjoibmFtZSI...
```

**Sortable fields are a whitelist, per feature.** `name`, `createdAt` and
`lastModifiedAt` — and nothing else. An unknown field, or one that exists on the row
but is not on the list, is `400` `sort.invalid` with the legal names in the message. No
caller string ever reaches a LINQ expression: the field name is canonicalised against
the whitelist in the application layer, and the persistence layer turns it into a
column with an exhaustive `switch` whose `default` arm throws. A field is on the
whitelist only if it is cheap to order by, which is why the list is short and why each
entry has a composite index behind it (`(OwnerId, <field>, Id)`).

**Every order ends in a unique tiebreaker.** `Id` is appended to every `ORDER BY`,
always. Without it two rows with equal sort keys can swap places between two page
reads, so one row is served twice and another never — which is a silent wrong answer,
not a slow one.

**Search is bounded and happens in the database.** It is a case-insensitive contains
on the list name, via PostgreSQL `ILIKE` with `%`, `_` and `\` escaped, so a caller
sending `%` matches lists literally containing `%` rather than matching everything. It
is **not** accent-insensitive: that needs the `unaccent` extension and a functional
index, which is a deployment's decision, and doing it in memory instead would mean
reading every row to filter a page of twenty.

**Two paging modes, and the difference matters.**

- **Offset** (`page`/`pageSize`) answers `totalCount`, `totalPages` and `hasNextPage`,
  which is what a page-number UI needs. It is unstable under concurrent writes — a row
  inserted before your position shifts everything down, so page 2 can repeat a row from
  page 1 — and it gets slower the deeper you go, because the database still walks the
  rows it skips.
- **Cursor** (`paging=cursor`, then follow `nextCursor`) resumes from the last row it
  served, so an insert elsewhere cannot shift your position, and page 500 costs what
  page 1 costs. It answers **no `totalCount`**: counting the whole match set is a second
  scan of it, which is the cost keyset paging exists to avoid. It allows **one** sort
  term (plus the tiebreaker), and only over a field marked keyset-capable —
  `lastModifiedAt` is not, because it is nullable and a comparison against `NULL` would
  skip the row the cursor was minted from instead of resuming at it.

The cursor is opaque but **not signed**. It does not need to be: it carries only values
from a row the caller was already served, and the read query filters by owner regardless
of what the cursor claims. A tampered cursor is a `400` `cursor.invalid`, never a 500
and never another user's rows.

**Pagination metadata lives in the body**, in the `PagedResult` envelope — there are no
RFC 8288 `Link` headers, so there is exactly one statement of where the next page is.
See `docs/adr/0016`.

Every bound above is a `400` carrying its own code — `paging.invalid`, `sort.invalid`,
`filter.invalid`, `cursor.invalid` — so a client can tell which rule it broke. A value
of the wrong *type* (`page=abc`) is instead the framework's `request.malformed`: one
vocabulary for a malformed request, one for a broken rule.

To give a new feature this contract, it declares an `ICollectionPolicy` — see
`docs/ADDING-A-FEATURE.md`. Why the filter surface is typed rather than an expression
language is `docs/adr/0015`.

### Retrying a `POST` safely — `Idempotency-Key`

A client that retries a create through a flaky network must not create twice. Send an
`Idempotency-Key` header (any opaque string up to 128 characters — a UUID is the obvious
choice) on `POST /api/v1/todo-lists` or `POST /api/v1/todo-lists/{id}/items`:

```bash
curl -X POST "$API/todo-lists" -H "Authorization: Bearer $TOKEN" \
     -H 'Idempotency-Key: 9f1c7e2a-0c1b-4f0a-9a3e-2b6d5c4a8e10' \
     -H 'Content-Type: application/json' -d '{"name":"Groceries"}'
```

| Situation | Answer |
|---|---|
| First use of the key | The request runs normally |
| Same key, same body, after it completed | The original status, body and `Location`, plus `Idempotency-Replayed: true` — the action does **not** run again |
| Same key, **different** body | `409` `idempotency.keyReused` |
| Same key while the first is still running | `409` `idempotency.inProgress` |
| Same key, but the first attempt **failed** | Runs normally — a failed attempt releases its claim, so a corrected retry is not blocked |
| No key at all | Runs normally; the header is available, not compulsory |

Keys are scoped **per user**, so two callers may use the same key string without colliding.
Only actions marked `[Idempotent]` participate — the auth endpoints deliberately do not,
because replaying a login would mean storing a bearer token in the database.

Keys are remembered for `Idempotency:Retention` (24 hours by default), but **nothing prunes
them for you**: schedule `DELETE /api/v1/maintenance/idempotency-keys/expired`, which requires
the `Administrator` policy and is also this template's worked example of policy-based
authorisation.

### Health

| Route | Checks | Anonymous |
|---|---|---|
| `/health` | nothing — answers "is the process up" | yes |
| `/health/ready` | the database, through `AppDbContext` (`ready` tag) | yes |

Liveness deliberately touches no dependency, so an orchestrator does not restart a
healthy API because the database was briefly unreachable. The Compose and Dockerfile
healthchecks both target `/health`.

### Authorisation is default-deny

`Program.cs` installs an authorization fallback policy requiring an authenticated
user. An endpoint is protected **unless it explicitly opts out** with
`[AllowAnonymous]`; only the auth controller and the two health endpoints do.

One consequence to know about: because the fallback policy also applies when no
endpoint matched, an **unknown route returns 401 to an anonymous caller, not 404**.
That is not a bug, but it will surprise a client developer, so say so in your API
docs.

### Errors are RFC 7807, and clients branch on `code`

Every failure is a `ProblemDetails` body with a stable, dotted `code` extension
member. **Branch on `code`, never on the prose in `detail`** — the prose is not part
of the contract.

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not found",
  "status": 404,
  "detail": "No to-do list with id '…' was found.",
  "code": "todoList.notFound"
}
```

| `code` | Status |
|---|---|
| `auth.required` | 401 |
| `auth.login.invalidCredentials` | 401 — one answer for unknown address, wrong password, unconfirmed email and lockout alike |
| `auth.refreshToken.invalid` | 401 — unknown, expired, revoked or replayed |
| `auth.confirmEmail.invalid` | 400 |
| `auth.register.unavailable` | 409 |
| `auth.register.rejected` | 400 |
| `todoList.notFound` / `todoItem.notFound` | 404 — a list owned by somebody else is also 404, so ids cannot be enumerated |
| `todoList.invariantViolated` | 409 |
| `todoList.validationFailed` / `paging.invalid` | 400 |
| `precondition.failed` | 412 — the `If-Match` a write named is stale, unrecognised, or `*` against a missing/foreign resource |
| `precondition.required` | 428 — only when `Concurrency:IfMatch` is `Required`; the write named no version at all |
| `precondition.malformed` | 400 — `If-Match` is present but is neither `*` nor a comma-separated list of quoted entity tags |
| `rateLimit.exceeded` | 429, with a `Retry-After` header |

A 500 carries no exception text — only a sanitised message and a `traceId` that
correlates with the full stack trace in the logs.

Every error response is served as `application/problem+json`.

### Rate limits

| Scope | Limit | On rejection |
|---|---|---|
| `api/v1/auth/*` | 10 requests/minute per IP | 429 + `Retry-After: 60` + `code: rateLimit.exceeded` |
| Everything else | 300 requests/minute per IP | same |

Fixed windows, partitioned by `RemoteIpAddress`. **Behind a reverse proxy this needs
`ForwardedHeaders` configured**, otherwise every request appears to come from the
proxy and the whole world shares one partition. Not configured in this template — it
depends on your topology.

## Configuration

Full reference: **[docs/CONFIGURATION.md](docs/CONFIGURATION.md)**.

| Section | Purpose |
|---|---|
| `ConnectionStrings:Default` | the single PostgreSQL connection string |
| `Jwt` | signing key, issuer, audience, access-token lifetime |
| `RefreshToken` | refresh-token lifetime |
| `Identity` | password, lockout and sign-in policy |
| `Email` | SMTP transport, including `AllowInsecureTransport` |
| `EmailConfirmation` | confirmation link target and subject |
| `IdentitySeed` | development-only admin seeding |
| `Cors` | allowed browser origins |

Each binds to an options class with a validator registered via `.ValidateOnStart()`.
A missing or out-of-range value fails the host at startup with a message naming the
exact key — verified: blanking `EmailConfirmation:ConfirmEmailUrl` and shortening
`Jwt:Key` produces
`'Jwt:Key' must be at least 32 bytes long to sign HS256 tokens.` and
`'EmailConfirmation:ConfirmEmailUrl' is required.` before Kestrel binds.

`appsettings.json` is tracked and holds **no secrets** — every secret-shaped value in
it is an empty string, which is why that file alone will not boot the app.

### One trap worth knowing: `Email:Security`

Startup validation rejects **every** SMTP mode that can end up sending in the clear
— `None`, `StartTlsWhenAvailable` **and `Auto`** — for a host that is not loopback.
`Auto` is on that list because MailKit resolves it to `StartTlsWhenAvailable` on any
port but 465, so permitting it would reopen the same silent downgrade under a
friendlier name.

A containerised relay such as mailpit is not loopback and speaks no TLS at all, so
plaintext against it must be **stated outright**:

```
Email__Security=None
Email__AllowInsecureTransport=true
```

`docker-compose.yml` and `.env.example` do exactly that. For a real relay use
`StartTls` or `SslOnConnect` and leave `AllowInsecureTransport` at `false`.

## Tests

Test projects live under `Tests/`, mirroring the production tree one-for-one, and use
**xunit.v3 + Shouldly + NSubstitute** (not FluentAssertions), with `NetArchTest.Rules`
for the dependency-direction tests and `Testcontainers.PostgreSql` for integration
tests.

```bash
dotnet test AppTemplate.sln
```

With coverage, as CI runs it:

```bash
dotnet test AppTemplate.sln \
  --configuration Release \
  --logger "trx;LogFileName=test-results.trx" \
  --results-directory TestResults \
  --collect:"XPlat Code Coverage"
```

Integration tests start a real PostgreSQL in Docker, so **Docker must be running**.
CI runs on `ubuntu-latest`, which provides a Docker daemon; the hosted macOS and
Windows runners do not.

All four test projects are listed in `AppTemplate.sln`, so `dotnet test AppTemplate.sln` runs every one of
them. Keep it that way: a project on disk but absent from the solution is skipped silently,
and CI asserts against exactly that (see the "Assert test projects exist and are in the
solution" step in `.github/workflows/ci.yml`).

> If the test projects opt into Microsoft.Testing.Platform
> (`<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`), the
> `--logger` / `--collect` flags change shape and both this section and
> `.github/workflows/ci.yml` need updating together.

## Database migrations

`dotnet-ef` is pinned as a local tool, so use the manifest rather than a global
install:

```bash
dotnet tool restore
dotnet ef --version      # 10.0.10
```

**Migrations are applied at startup in Development only.** In any other environment
the API starts without touching the schema: migrating from the process that serves
requests needs DDL rights at runtime and races between replicas on
`__EFMigrationsHistory`. Apply them as a deliberate deployment step —
`dotnet ef database update`, or a migration bundle. See
[ADR 0009](docs/adr/0009-no-migrations-at-startup-in-production.md).

### One migration set

There is one `DbContext` and one migrations history table. The design-time factory reads
`ConnectionStrings__Default` from the **environment** — set it first:

```bash
# bash
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=appuser_local_dev"
```

```powershell
# PowerShell
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=appuser_local_dev"
```

There is **one** `DbContext` — `AppDbContext`, in `AppTemplate.Infrastructure.Persistence` — and
therefore one migrations history. No `--context` argument is needed anywhere:

```bash
dotnet tool restore

dotnet ef migrations add <Name> \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --output-dir Migrations

dotnet ef migrations list   --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
dotnet ef database update   --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
dotnet ef migrations remove --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

The features still separate themselves by **schema** — `identity` for ASP.NET Identity's
tables, `todo` for the to-do list feature's — declared table by table in each feature's
`IEntityTypeConfiguration`. The single `__EFMigrationsHistory` sits in `public`, because it
belongs to neither.

`AppDbContextFactory` is a design-time factory reading `ConnectionStrings__Default` from the
environment, with a localhost fallback, so the check that the model and the migrations still
agree needs no running host and no database:

```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

`migrations remove` only undoes a migration that has not been applied.

## Project layout

```
AppTemplate.sln                         solution (classic format)
global.json                    SDK pin
Directory.Build.props          shared TFM, nullable, warnings-as-errors
Directory.Packages.props       Central Package Management — all versions
.config/dotnet-tools.json      pinned local tools (dotnet-ef)
docker-compose.yml             db + mailpit + api
.env.example                   template for .env  (cp .env.example .env)

Src/
  Domain/
    AppTemplate.Domain/                        aggregates, value objects, domain events
                                      -> ZERO dependencies, no NuGet packages
  Application/
    AppTemplate.Application/                   use cases, ports, Result/Error
                                      -> Domain only
  Infrastructure/
    AppTemplate.Infrastructure.Persistence/    ALL persistence: the one DbContext, the interceptor
                                      pipeline, the unit of work, and per-feature models,
                                      mappers, repositories, queries and stores
                                      -> Application
    AppTemplate.Infrastructure.Identity/       ASP.NET Identity policy, JWT, refresh-token rotation
                                      (no database of its own)
                                      -> Application + Persistence
    AppTemplate.Infrastructure.Email/          MailKit SMTP sender, email options
                                      -> Application
    AppTemplate.Infrastructure.InMemory/       in-memory port implementations for tests/demo
                                      -> Application
  Presentation/
    AppTemplate.Api/                           controllers, composition root, Dockerfile
                                      -> Application + every module

Tests/
  Domain/AppTemplate.Domain.UnitTests/           the aggregate, in memory
  Application/AppTemplate.Application.UnitTests/ use cases against test doubles
  Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/
                                        the domain <-> row mapper, reflection-driven
  Architecture/AppTemplate.Architecture.Tests/   layer/module rules + container composition
  Integration/AppTemplate.Api.IntegrationTests/  the real host over HTTP, real PostgreSQL

docs/                          ARCHITECTURE.md, CONFIGURATION.md, adr/
```

The directory under `Src/` names the **layer**; the project inside it keeps its own name
and its own root namespace. Moving a project between layer folders therefore changes no
namespace — the disk path and the root namespace are independent.

### Inside a project: feature first, responsibility second

Within `AppTemplate.Application` and `AppTemplate.Api`, the top-level partition is the **business feature**,
and only inside a feature is code grouped by what it does:

```
AppTemplate.Application/
  Common/                       Result, Error, PagedResult, Abstractions/ (cross-feature ports)
  Features/
    TodoLists/
      Dtos/                     read-model shapes
      Ports/                    ITodoListRepository, ITodoListQueries
      UseCases/Commands/        Create, Rename, Delete, AddItem, CompleteItem, RemoveItem
      UseCases/Queries/         GetTodoLists, GetTodoList
      Validators/               FluentValidation rules for the commands
      TodoListErrors.cs         the feature's failure vocabulary
    Auth/
      Dtos/                     response shapes
      Errors/                   AuthErrors — the vertical's failure vocabulary
      Ports/                    IUserAccounts, IEmailConfirmationTokens, IAccessTokenIssuer,
                                IRefreshTokenGrants, IConfirmationEmailComposer
      UseCases/Commands/        Register, Login, RefreshAccessToken, ConfirmEmail,
                                ResendConfirmationEmail, Logout — each with the request
                                record it accepts
      Validators/               one per request
```

A command or request record lives **in the file of the single use case that accepts it**,
because it is that use case's signature. Only outbound shapes get a folder of their own.

In `AppTemplate.Domain`, `AppTemplate.Application` and `AppTemplate.Api` there is deliberately **no `Services/`,
`Interfaces/`, `DTOs/`, `Helpers/`, `Managers/` or `Factories/` folder at the project
root.** Grouping by technical type at the top level is what this template was rescued from:
it put the six files that implement one feature in six different directories, so no change
was ever local and no folder ever told you what the application does. A responsibility
folder is legitimate only *inside* a feature, where it partitions something that is already
cohesive.

A single-capability infrastructure module is the exception, because it *is* one
responsibility: it has no features to partition, so its root folders name the parts of that
one capability (`Bearer/`, `Notifications/`, `Options/`, `Tokens/`, `Users/`, `Templates/` in
`AppTemplate.Infrastructure.Identity`; `Options/` and `Services/` in `AppTemplate.Infrastructure.Email`).

`AppTemplate.Infrastructure.Persistence` holds more than one capability, so it is partitioned the same
way the inner layers are — feature first:

```
AppTemplate.Infrastructure.Persistence/
  Common/                       the cross-cutting mechanisms, which name no feature
    Contexts/                   AppDbContext (the model's composition root), design-time
                                factory, connection-string helper
    Auditing/                   the audit interceptor
    DomainEvents/               dispatcher, dispatch interceptor, consumer + source contracts
    Mapping/                    IAggregateFlusher and the flush interceptor
    Time/                       the system clock
    UnitOfWork/                 EfUnitOfWork, and the EF -> ConcurrencyConflictException
                                translation
  Features/
    TodoLists/
      Models/                   TodoListRecord, TodoItemRecord, TodoItemTagRecord
      Configurations/           IEntityTypeConfiguration for each record
      Mappers/                  ITodoListMapper: aggregate <-> rows
      Tracking/                 the per-request identity map, flusher and event source
      Repositories/             TodoListRepository : ITodoListRepository
      Queries/                  TodoListQueries : ITodoListQueries (rows -> DTOs, in SQL)
      DomainEvents/             the feature's own consumers
    Identity/
      Models/                   AppUser, AppRole, RefreshToken
      Configurations/           table and index mapping, one schema per feature
      Stores/                   IRefreshTokenStore
      Seeding/                  IIdentitySeeder and its options
  Migrations/                   one history
  PersistenceModule.cs
```

An architecture test asserts the rule that layout encodes: nothing under `Common/` may name a
feature, with `AppDbContext` as the single documented exception — it applies every feature's
configuration, which is what makes it the model's composition root.

The word "Module" is kept for exactly one thing: dependency-injection registration classes
(`PersistenceModule`, `IdentityModule`, `EmailModule`). That is a composition concept, not a
business partition.

**The dependency rule: source dependencies point inward, always.** `AppTemplate.Domain`
references nothing — not even a NuGet package. `AppTemplate.Application` references only
`AppTemplate.Domain`. Infrastructure modules reference `AppTemplate.Application` and may reference
`AppTemplate.Infrastructure.Persistence`; **Persistence never references a module back**, and
modules do not reference each other. Only `AppTemplate.Api` knows about all of them, and only
to wire them up. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Ports and TLS

| Context | HTTP | HTTPS |
|---|---|---|
| `dotnet run` — `http` profile | 5187 | — |
| `dotnet run` — `https` profile | 5187 | 7004 (dev certificate) |
| Container / Compose | 8080 | **none** |

**The container speaks plain HTTP on 8080 and nothing else.** TLS is terminated
upstream — by an ingress controller, reverse proxy or cloud load balancer.

HTTPS redirection is **not** installed in the pipeline, on purpose. The container
listens on plain 8080, so `UseHttpsRedirection` would 307 the orchestrator's health
probe and every internal call. Enforce HTTPS at the ingress instead.

The previous Dockerfile declared `EXPOSE 8081` for HTTPS and Compose published it,
but no certificate was ever provisioned, `ASPNETCORE_HTTPS_PORTS` was never set and
nothing bound the port: a published port that could not answer. Half-configured TLS
is worse than none, because it reads as present.

If you need TLS inside the container, mount a certificate and set
`ASPNETCORE_HTTPS_PORTS` with `Kestrel__Certificates__Default__*` explicitly. Do not
just add an `EXPOSE`.

For local HTTPS on the host:

```bash
dotnet dev-certs https --trust
dotnet run --project Src/Presentation/AppTemplate.Api --launch-profile https
```

## Container image

```bash
docker build -f Src/Presentation/AppTemplate.Api/Dockerfile -t app-template-api:local .
```

The build context is the **repository root**, not `Src/Presentation/AppTemplate.Api` — `Directory.Build.props`,
`Directory.Packages.props` and `global.json` must be copied before `dotnet restore`
or Central Package Management fails.

- Multi-stage: `sdk:10.0.302-noble` builds, `aspnet:10.0.10-alpine3.23` runs. Both
  pinned to a patch version.
- Runs as **non-root** (`USER $APP_UID`). The alpine variant does not set `User`
  itself, so that line is load-bearing.
- Publishes portable IL on the build machine's architecture, so one build serves
  every target; for multi-arch:
  `docker buildx build --platform linux/amd64,linux/arm64 -f Src/Presentation/AppTemplate.Api/Dockerfile .`
- `HEALTHCHECK` on `/health`.
- `.dockerignore` keeps `appsettings.Development.json`, `.git` (including
  `.git/config`, which can carry a token in a remote URL), `Tests/`, certificates and
  keys out of the image layers.

> The Dockerfile's `COPY` list names the project files one by one. **It must be
> updated when the infrastructure split lands**, or restore inside the image will
> fail on a missing `.csproj` even though the local build is fine. The `docker` job in
> CI is what catches that.

## Supply chain

`Directory.Build.props` sets `NuGetAudit` with `NuGetAuditMode=all` and
`NuGetAuditLevel=low`, so a package with **any** known advisory fails the build, not
just a critical one.

That is why `Directory.Packages.props` pins `Microsoft.OpenApi` to **2.7.6** under a
`Security pins` label even though nothing references it directly:
`Microsoft.AspNetCore.OpenApi` 10.0.10 resolves `Microsoft.OpenApi` 2.0.0, which
carries the high-severity **GHSA-v5pm-xwqc-g5wc**, and at audit level `low` that is a
restore error. `CentralPackageTransitivePinningEnabled` is what makes the pin bind to
a transitive dependency. 2.7.6 is deliberately not 3.x — that is a different major
than the one `Microsoft.AspNetCore.OpenApi` was compiled against.

**Do not remove that pin** without first checking that the ASP.NET Core package ships
a patched dependency itself. `dotnet list package --vulnerable --include-transitive`
is the check, and CI runs it on every push.

## CI

`.github/workflows/ci.yml`, on push and PR to `main`:

| Job | Does |
|---|---|
| `build-test` | restore, `dotnet format --verify-no-changes`, build (warnings are errors), run the tests, enforce the coverage floor in `coverage.minimum` |
| `repository-hygiene` | asserts every path cited in the docs exists, and that the workflows are structurally sound |
| `vulnerable-packages` | `dotnet list package --vulnerable --include-transitive`, failing on any hit |
| `docker` | builds the image, then asserts it does not run as root and exposes only 8080 |
| `compose` | `docker compose config` against `.env.example` |

All actions are pinned to a commit SHA, `permissions:` is `contents: read`, and a
concurrency group supersedes superseded runs. No CI step passes a flag that would
turn warnings back into warnings: `TreatWarningsAsErrors=true` lives in
`Directory.Build.props` and must stay authoritative.

Coverage is collected over every test project **except** `AppTemplate.Architecture.Tests`, and that split is
deliberate: NetArchTest resolves each type through `Type.GetType(name, throwOnError: true)`, which
fails against a Coverlet-instrumented assembly. Under the collector 7 of its 40 rules throw; without
it all 40 pass. Every test still runs exactly once. Do not merge those two steps back together.

`.github/workflows/release.yml` runs on a `v*.*.*` tag: it re-runs the gate, publishes a multi-arch
image to GHCR with an SBOM and a signed provenance attestation, and uploads a self-contained
migration bundle built from the same commit. It needs no secret beyond the automatic `GITHUB_TOKEN`.

## Where to look next

| File | For |
|---|---|
| [CONTRIBUTING.md](CONTRIBUTING.md) | the working agreement: layout rules, the six gates, comment policy, how to add a feature |
| [docs/ADDING-A-FEATURE.md](docs/ADDING-A-FEATURE.md) | the vertical walkthrough: aggregate → EF model → mapper → tracker → store → use case → controller → tests → migration |
| [SECURITY.md](SECURITY.md) | what the template provides, **what a deployment must still do**, and the known gaps |
| [CHANGELOG.md](CHANGELOG.md) | what has changed, and what "breaking" means for a template |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | how the layers fit together |
| [docs/CONFIGURATION.md](docs/CONFIGURATION.md) | every configuration key, its default, and what happens when it is wrong |
| [docs/adr/](docs/adr/) | one record per decision you could reasonably have made differently, including the rejected options |
| [AppTemplate.Api.http](AppTemplate.Api.http) | the whole API walkthrough, executable |
| [tasks.ps1](tasks.ps1) | thin wrappers over the real `dotnet` commands — it prints each one before running it |

If you read only one of them before deploying, read `SECURITY.md`: its second section is longer than
its first, and that is the honest shape of the thing.

## Licence

MIT — see [LICENSE](LICENSE).
