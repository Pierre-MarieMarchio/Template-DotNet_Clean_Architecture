# Clean Architecture .NET Template

A production-shaped starting point for a .NET 10 HTTP API: Clean Architecture
layering, PostgreSQL via EF Core, ASP.NET Identity with JWT access tokens and
opaque rotating refresh tokens, default-deny authorisation, RFC 7807 errors, and
configuration that fails fast when it is wrong.

The sample domain is three features — a to-do list with real invariants and domain
events, a flat reminders aggregate, and a file store whose two halves live in
different stores — enough to show an aggregate, a read/write port split and a
`Result`-based error policy, without becoming an application you have to delete.

> - Layer boundaries and the reasoning behind them: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
> - Every configuration key: [docs/CONFIGURATION.md](docs/CONFIGURATION.md)
> - Running it on Kubernetes: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)
> - Deleting the `TodoLists`, `Reminders` and `Files` examples once you have read them: [docs/REMOVING-THE-EXAMPLE-FEATURES.md](docs/REMOVING-THE-EXAMPLE-FEATURES.md)

## What is in the box

- **.NET 10** (`net10.0`), SDK pinned by `global.json`
- **PostgreSQL** through `Npgsql.EntityFrameworkCore.PostgreSQL`, one connection
  string, one `DbContext`, one migrations history, five schemas
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

dotnet run Tools/Tasks.cs bootstrap   # required once — see the first note below
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

- **`dotnet run Tools/Tasks.cs bootstrap` is not optional, and it is the first thing to
  run.** Sorted `using` directives are ordered alphabetically, so where a project's own
  namespace falls relative to `FluentValidation`, `Microsoft.*` and the rest depends on
  the name you chose. `AppTemplate` sorts one way, `Acme.OrderManagement` another. Until
  you run it once, `dotnet format --verify-no-changes` fails — and since that is the
  *first* step of the CI workflow you inherit, your first push fails with it. One run
  fixes it permanently. It cannot be fixed in the template itself: no single committed
  ordering is correct for every possible name.
- **The `TodoLists`, `Reminders` and `Files` features ship by default** as the worked
  examples for [docs/ADDING-A-FEATURE.md](docs/ADDING-A-FEATURE.md): one aggregate
  with child entities, one flat, and one whose two halves live in different stores —
  metadata in PostgreSQL, bytes behind a port to an S3-compatible object store. There is no generator switch to exclude them;
  [docs/REMOVING-THE-EXAMPLE-FEATURES.md](docs/REMOVING-THE-EXAMPLE-FEATURES.md)
  is the verified procedure, and says what stops being demonstrated once they go.
- `dotnet new uninstall <path-to-this-repository>` removes the template again.

`.github/workflows/ci.yml`'s `template` job runs this exact install → generate →
build → test sequence, under a different name, on every push — a template that
generates a broken project is worse than a repository to clone, so that flow is a
CI gate, not just a manual step.

## Quick start — Docker

Brings up PostgreSQL, a mail catcher, an S3-compatible object store, the API and the
Worker. Nothing is installed on the host beyond Docker.

```bash
git clone <your-fork-url> && cd <the-directory-it-created>

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
| MinIO console — uploaded files land here | <http://localhost:9001> |
| PostgreSQL | `127.0.0.1:5432` |
| Mailpit SMTP | `127.0.0.1:1025` |
| MinIO S3 API | `127.0.0.1:9000` |

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

# 3. log in — the body is tagged {"status":"authenticated","tokens":{…}}, so the pair is nested
TOKEN=$(curl -s -X POST http://localhost:5187/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","password":"Passw0rd!x"}' | jq -r .tokens.accessToken)

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

To run locally against your own database, two values are all you have to supply:
`ConnectionStrings:Default` and `Jwt:Key`. They go in `dotnet user-secrets` —
`AppTemplate.Api.csproj` already carries a `UserSecretsId`, so the store exists and
overrides both appsettings files. A deployed environment supplies them, and every other
blank in `appsettings.json`, as environment variables instead, with `__` as the section
separator (`ConnectionStrings__Default`, `Jwt__Key`, `Cors__AllowedOrigins__0`).

**Never put a secret in `appsettings.json` or `appsettings.Development.json` — both are
tracked in git.** The commands, the full key list and what each value is validated against
are in
[docs/CONFIGURATION.md](docs/CONFIGURATION.md#secrets-use-dotnet-user-secrets).

> **Development seeding creates an administrator, and it is off by default.**
> `IdentitySeed:Enabled` is `false`, `IdentitySeeder` throws if it is turned on outside the
> Development environment, and `IdentitySeed:AdminPassword` has no default so enabling
> seeding without one fails startup instead of creating a guessable admin. Never enable it
> anywhere reachable from the internet. The four keys and how to set them locally:
> [docs/CONFIGURATION.md](docs/CONFIGURATION.md#identityseed--development-only).

## The API surface

All routes are versioned. `api-supported-versions: 1.0` comes back on every
response, and the version segment substitutes into the route
(`api/v{version:apiVersion}/…`).

### Authentication — `api/v1/auth/*`

Ten of the controller's eighteen actions are explicitly `[AllowAnonymous]`; the
other eight require `[Authorize]` (`logout-all`, `me`, `change-password`, the three
`two-factor/*` actions, `change-email`, `confirm-email-change`). Sixteen of the
eighteen are rate-limited to **10 requests per minute per client IP**; `logout-all`
and `me` deliberately fall to the global limiter instead, since neither is an attempt
at a credential.

| Method | Route | Success | Notes |
|---|---|---|---|
| POST | `/api/v1/auth/register` | 200 | Body carries `confirmationEmailSent`; the account is committed before the mail is sent, so a delivery failure is recoverable, not fatal. |
| POST | `/api/v1/auth/login` | 200 | Tagged by a `status` field a client reads rather than guessing from which fields are present: `authenticated` nests the four token fields under `tokens`, `twoFactorRequired` carries a `challengeToken` instead. |
| POST | `/api/v1/auth/login/two-factor` | 200 | Exchanges the challenge token and a code for the same response shape as `login`. |
| POST | `/api/v1/auth/login/external` | 200 | The client runs the provider's OAuth/PKCE flow itself and posts `provider` and the `idToken`; the API verifies it against that provider's JWKS and mints its own pair. Same `status` tag as `login`, plus `accountCreated`. |
| POST | `/api/v1/auth/refresh` | 200 | Consumes the presented refresh token and returns a new pair. |
| POST | `/api/v1/auth/confirm-email` | 204 | **POST with a JSON body**, not a GET with a query string. |
| POST | `/api/v1/auth/resend-confirmation-email` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/logout` | 204 | Revokes the presented refresh token. Idempotent. |
| POST | `/api/v1/auth/logout-all` | 204 | Authenticated. Revokes every refresh token grant the caller holds. |
| GET | `/api/v1/auth/me` | 200 | Authenticated. The caller's own profile; takes no input. |
| POST | `/api/v1/auth/change-password` | 204 | Authenticated. The current password is presented again as proof the session is not a stolen token. |
| POST | `/api/v1/auth/two-factor/setup` | 200 | Authenticated. Provisions a shared key; arms nothing on its own. **It rotates the security stamp, so the access token that called it stops working** — sign in again before calling `two-factor/confirm`. |
| POST | `/api/v1/auth/two-factor/confirm` | 200 | Authenticated, and requires the current password: arming a second factor is the irreversible direction, so a stolen session alone must not do it. Confirms enrollment, arms two-factor sign-in, and returns ten recovery codes shown once. |
| POST | `/api/v1/auth/two-factor/disable` | 204 | Authenticated. Requires the current password. |
| POST | `/api/v1/auth/change-email` | 204 | Authenticated. Requires the current password; mails a token to the new address. |
| POST | `/api/v1/auth/confirm-email-change` | 204 | Authenticated. Confirms the pending change from the token mailed to the new address. |
| POST | `/api/v1/auth/forgot-password` | 204 | Always 204, whether or not the address exists. |
| POST | `/api/v1/auth/reset-password` | 204 | **POST with a JSON body**, not a GET with a query string. |

**The refresh token is in the response body, not a cookie.** It is an opaque
32-byte CSPRNG value, base64url-encoded — not a JWT — and only its SHA-256 hash is
stored. Every presentation rotates it. Presenting one that was already rotated or
revoked is treated as theft: the **entire token family for that user is revoked** and
the request fails. Verified: replaying a consumed token returns 401, and the
token it had been rotated into stops working too.

### To-do lists — `api/v1/todo-lists/*`

Authentication required (no opt-out). One controller for one aggregate root; items
are addressed **through their list**, because that is what the aggregate boundary
means — there is no route that reaches an item without naming its list. Every write
below answers `200` with the changed representation and its new `ETag`, except
creating a list or an item (`201`) and deleting a list (`204`).

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/todo-lists?page=1&pageSize=20&sort=createdAt:desc` | 200 — paged summaries of the caller's own lists |
| GET | `/api/v1/todo-lists/{todoListId}` | 200 — the list with its items and tags |
| GET | `/api/v1/todo-lists/{todoListId}/items` | 200 — every item of the list |
| GET | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 200 |
| POST | `/api/v1/todo-lists` | 201 + `Location` |
| PUT | `/api/v1/todo-lists/{todoListId}` | 200 — rename; the id comes from the route, never the body |
| DELETE | `/api/v1/todo-lists/{todoListId}` | 204 |
| POST | `/api/v1/todo-lists/{todoListId}/items` | 201 + `Location` |
| PUT | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 200 — replaces title and description |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/complete` | 200 |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reopen` | 200 |
| DELETE | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}` | 200 — answers with the list: the item this route named no longer exists to have its own representation |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags` | 200 |
| PUT | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags` | 200 — replaces the whole tag set |
| DELETE | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags/{tag}` | 200 |

### Reminders — `api/v1/.../reminders` and `api/v1/reminders/*`

Authentication required (no opt-out) — like `TodoListsController`, `RemindersController`
declares neither `[Authorize]` nor `[AllowAnonymous]` and relies entirely on the
default-deny fallback policy. A reminder is its own aggregate root, addressed
independently of the list or item it is about once scheduled — scheduling and listing
go through the item, rescheduling and cancelling go through the reminder's own id.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | 200 — every reminder of the caller's scheduled for that item. **200 with an empty list, not 404, for an item that does not exist or is somebody else's** — the one route into an item that does not answer 404, because a reminder outlives the item it is about and this is the only route that can show a cancelled one. Nothing leaks: a stranger sees the same empty list either way |
| POST | `/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders` | 201 + `Location` — points at the collection above; there is no single-reminder `GET` |
| PUT | `/api/v1/reminders/{reminderId}` | 200 — reschedule |
| DELETE | `/api/v1/reminders/{reminderId}` | 204 — cancel |

### Files — `api/v1/files/*`

Authentication required (no opt-out). **No byte of any file passes through this API, in
either direction** — that is the one thing to understand before calling anything here.
The API signs URLs and the client talks to the object store directly, so depositing a
file is two requests and reading one back is a redirect.

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/files?page=1&pageSize=20&sort=registeredAt:desc` | 200 — paged summaries of the caller's own files |
| GET, HEAD | `/api/v1/files/{fileId}` | 200 — the file's metadata, and the `ETag` the two writes below are conditioned on; 304 to a matching `If-None-Match` |
| GET | `/api/v1/files/{fileId}/content` | **302** + `Location` — a short-lived signed URL, and no body |
| POST | `/api/v1/files` | 201 + `Location` — reserves a place and returns the upload grant |
| POST | `/api/v1/files/{fileId}/confirm` | 200 — the file's metadata, with its new version |
| DELETE | `/api/v1/files/{fileId}` | 204 |

**Depositing, in order.**

1. `POST /api/v1/files` with metadata only — `name`, `declaredMediaType`, `sizeInBytes`
   and `checksum` (SHA-256, 64 hexadecimal characters). The answer is the file's `id` and
   an `upload` grant: a signed `url`, the `method` the signature covers, the
   `requiredHeaders` the deposit must send back verbatim, and an `expiresAt`.
2. Send the bytes straight at that URL, with the grant's `method` (`PUT` in both shipped
   adapters) and its `requiredHeaders` unchanged. The signature covers the media type, the
   length and the checksum, so a deposit that does not match what was declared is refused
   by the store, with nothing written.
3. `POST /api/v1/files/{fileId}/confirm`, which asks the store what it actually holds and
   moves the file from `pending` to `deposited` only if that agrees with the declaration.

Splitting it across two API calls is forced rather than chosen:
`RequestLimits:MaxRequestBodyBytes` caps an inbound body at 64 KiB, and the idempotency
filter buffers and SHA-256s the whole body of every `POST` before a handler sees it. Every
body on this controller is metadata — a name, a media type, a length, a digest — a few
hundred characters whatever the file weighs, which is why no action here raises the limit.

**Confirming does not make the file readable.** It leaves the file `deposited` — the bytes
arrived and are the ones declared, but nothing has looked at them — until the Worker's
inspection pass has, and `FileWorker:InspectDepositedFilesInterval`, one minute by default,
is a latency a user feels. `availableAt` is `null` until then.

`status` has **four** values, and a client that branches on it must handle all of them:

| `status` | What it means |
|---|---|
| `pending` | Registered, an object key reserved, nothing deposited against it yet. The abandonment sweep eventually removes one that stays here. |
| `deposited` | **What `confirm` answers.** The bytes are present and match the declaration; no verdict has been reached on what they are. Not servable. |
| `available` | Inspected and cleared. The only state whose content can be fetched. |
| `quarantined` | Inspected and refused. Terminal — it never becomes available, however long the caller waits. |

Asking for content before a verdict is `409` `storedFile.notAvailable`; asking for content
that was examined and refused is `409` `storedFile.quarantined`.

**Reading is a redirect.** `GET /api/v1/files/{fileId}/content` answers `302` with a signed
URL, so an `<img>`, a download manager or `curl -L` follows it with no client code. That
`Location` is a bearer credential — whoever holds it reads the file, with no identity
attached — so the response is `Cache-Control: no-store` and the URL must not be logged,
stored or shared. `POST /api/v1/files` carries a signed *write* URL and is `no-store` for
the same reason.

**Registration is the endpoint `Idempotency-Key` exists for.** It is unaddressed creation:
a retry that is not recognised mints a second file, a second object key and a second grant.
`confirm` deliberately takes no key — it names the file it acts on, and the transition is
one-way, so a second call meets a file that is no longer pending and gets `409`.

**Conditional requests.** `GET`/`HEAD` on one file publishes its version as a strong `ETag`;
`confirm` and `DELETE` honour `If-Match` and answer `412` when it is stale, `428` when
`Concurrency:IfMatch` is `Required` and none was sent. `If-Match: *` is the useful form
here: a registration nothing was ever deposited against is removed by the abandonment
sweep, so a client resuming after a long upload gets `412` rather than a `404` it might
read as "wrong id". Registration takes no precondition — there is no resource yet to have
a version.

**What refuses a registration.** `409` `storedFile.quotaExceeded` covers the three bounds
one owner has: 20 uploads outstanding at once, 1 000 files, and 10 GiB of committed bytes
(a single file may not exceed 5 GiB). `409` also carries a value the domain refuses that
this layer cannot restate — a reserved device name, a wildcard media type, a checksum of
the right length that is not hexadecimal.

Sorting is `name`, `registeredAt` and `availableAt`; `availableAt` is offset-only because
its column is nullable — asking for it with `paging=cursor` is a `400` `cursor.invalid`.
`search` matches the file name, `state` narrows to any one of the four values above.
Everything else in
[Collection queries](#collection-queries--sorting-filtering-paging) applies unchanged.

A file that belongs to somebody else answers exactly as an absent one does — `404`
`storedFile.notFound` — because a `403` next to a `404` is how an id becomes a probe.

### Account administration — `api/v1/auth/accounts/*`

`{role}` is a **role name**, and one role ships: `Admin`. `Administrator` is the name of the
*policy* these endpoints require, not of the role that satisfies it, and sending it answers
`400` `Role 'Administrator' does not exist.` The names live in `IdentityRoles`.

Requires the `Administrator` policy on the whole controller — an authenticated
non-admin gets `403`. Acting on somebody else's account: every action below refuses
with `403` when the target id names the caller.

| Method | Route | Success |
|---|---|---|
| POST | `/api/v1/auth/accounts/{userId}/lockout` | 204 — locks the account out indefinitely and rotates its security stamp |
| DELETE | `/api/v1/auth/accounts/{userId}/lockout` | 204 — lifts the lockout; a no-op on an account that was not locked |
| PUT | `/api/v1/auth/accounts/{userId}/roles/{role}` | 204 — grants a role; 400 if the role does not exist, or if the account already has it |
| DELETE | `/api/v1/auth/accounts/{userId}/roles/{role}` | 204 — revokes a role; 400 if the role does not exist, or if the account does not have it |
| DELETE | `/api/v1/auth/accounts/{userId}/two-factor` | 204 — disarms the account's second factor and rotates its security stamp; the way back for a lost phone and lost recovery codes |
| DELETE | `/api/v1/auth/accounts/{userId}` | 204 — deletes the account outright |

### Maintenance — `api/v1/maintenance/*`

| Method | Route | Success |
|---|---|---|
| DELETE | `/api/v1/maintenance/idempotency-keys/expired` | 200 — the number of rows removed |
| DELETE | `/api/v1/maintenance/refresh-tokens/expired` | 200 — the number of rows removed |

Requires the `Administrator` policy — an authenticated non-admin gets `403`. Together
with the six `api/v1/auth/accounts/*` actions above, these are the eight endpoints
whose authority is more than "authenticated plus ownership"; the two here exist
because the idempotency store and the refresh-token table each grow until something
prunes them. Schedule both.

**Conditional requests.** Every read of a single list, item or file publishes that
resource's version as a strong, opaque `ETag`; every write of one — a reminder's
reschedule and cancel, a file's confirm and delete included — honours `If-Match`, so a
stale edit, decided against a version somebody else has since changed, is refused with
`412` instead of silently overwriting it. `If-Match: *` asserts that the resource
exists, so a missing or someone-else's resource also answers `412`, not `404`. Sending
no `If-Match` at all is accepted unless `Concurrency:IfMatch` is set to `Required`, in
which case it is refused with `428` — see
[docs/CONFIGURATION.md](docs/CONFIGURATION.md#concurrency). `AppTemplate.Api.http` walks
through the whole round trip.

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
One statement of "is there a next page", in the body every client already parses.

Every bound above is a `400` carrying its own code — `paging.invalid`, `sort.invalid`,
`filter.invalid`, `cursor.invalid` — so a client can tell which rule it broke. A value
of the wrong *type* (`page=abc`) never reaches the application layer: model binding
refuses it with `request.validationFailed` and names the field in `errors`. That is the
same code a failed body validation carries, deliberately — one vocabulary for "this
request was rejected on its way in", and a specific code for each rule that has a
different remedy.

To give a new feature this contract, it declares an `ICollectionPolicy` — see
`docs/ADDING-A-FEATURE.md`. Why the filter surface is typed rather than an expression
language is that a grammar the client composes is a query planner you then own.

### Retrying a `POST` safely — `Idempotency-Key`

A client that retries a create through a flaky network must not create twice. Send an
`Idempotency-Key` header (any opaque string up to 128 characters — a UUID is the obvious
choice) on any of the four creates that carry `[Idempotent]`: `POST /api/v1/todo-lists`,
`POST /api/v1/todo-lists/{id}/items`, `POST /api/v1/todo-lists/{id}/items/{id}/reminders`
and `POST /api/v1/files`.

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

A completed key stays replayable for `Idempotency:Retention`, but **nothing prunes the
table for you**: schedule `DELETE /api/v1/maintenance/idempotency-keys/expired`. That
window, the claim lease behind `idempotency.inProgress` and the rest of the section are in
[docs/CONFIGURATION.md](docs/CONFIGURATION.md#idempotency).

### Health

| Route | Checks | Anonymous |
|---|---|---|
| `/health` | nothing — answers "is the process up" | yes |
| `/health/ready` | the database and the shutdown state, both tagged `ready` | yes |

Liveness deliberately touches no dependency, so an orchestrator does not restart a
healthy API because the database was briefly unreachable. The Compose and Dockerfile
healthchecks both target `/health`. What each probe should be wired to, and why readiness
fails the instant shutdown starts, is in
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

### Authorisation is default-deny

`Program.cs` installs an authorization fallback policy requiring an authenticated
user. An endpoint is protected **unless it explicitly opts out** with
`[AllowAnonymous]`; ten of `AuthController`'s eighteen actions and the two health
endpoints do — and, in Development only, so do the two OpenAPI endpoints (see
"Quick start — Docker" above).

One consequence to know about: because the fallback policy also applies when no
endpoint matched, an **unknown route returns 401 to an anonymous caller, not 404**.
That is not a bug, but it will surprise a client developer, so say so in your API
docs.

### Mail is written in the reader's language

Every mail this template sends — the three account mails and the reminder — ships one
template per language, and the **subject is that template's `<title>`**, so a subject and
a body can never end up in different languages. English and French ship.

| Where the language comes from | |
|---|---|
| `AppTemplate.Api` | The request's `Accept-Language`. The first well-formed tag wins; `q` values are not weighed, because a mail is written in one language. |
| A request that names none | `Localization:DefaultCulture`. |
| `AppTemplate.Worker` | `Localization:DefaultCulture`, always — a background pass has no request to read a preference from. |
| A language with no template | English, which every mail must ship. `fr-CA` reaches the `fr` template before falling back. |

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/register \
  -H 'Content-Type: application/json' -H 'Accept-Language: fr' \
  -d '{"userName":"alice","email":"alice@example.com","password":"Passw0rd!x"}'
# the confirmation mail in mailpit now reads "Confirmez votre adresse e-mail"
```

**Adding a language is adding files.** Drop `<Mail>EmailTemplate.<tag>.html` beside the ones
in `Src/Infrastructure/AppTemplate.Infrastructure.Identity/Features/Auth/Templates/` and
`Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders/`, and that language
is available — there is no list to update, because a list could name a language no template
backs. `EmailTemplateCoverageTests` refuses a language added to one folder and not the other,
and refuses two languages of one mail sharing a subject.

**Two things to know before changing this.** The repository builds with
`InvariantGlobalization=true`, so there is no `CultureInfo` to carry a language in and
`AppTemplate.Application.Common.Localization.CurrentLanguage` carries a BCP-47 tag instead.
And an `EmbeddedResource` named `*.fr.html` needs `WithCulture="false"` in the `.csproj`, or
MSBuild compiles it into a satellite assembly and every mail throws at the first send.
[docs/CONFIGURATION.md](docs/CONFIGURATION.md#localization) has the rest, including where a
stored per-account preference would plug in.

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

**Authentication and authorisation**

| `code` | Status |
|---|---|
| `auth.required` | 401 — no token, an expired one, or a route that matched nothing |
| `auth.forbidden` | 403 — authenticated, but the endpoint's policy is not satisfied, or the action refuses to target the caller's own account |
| `auth.login.invalidCredentials` | 401 — one answer for unknown address, wrong password, unconfirmed email and lockout alike |
| `auth.login.invalidTwoFactorChallenge` | 401 — the challenge token is unknown, spent or expired |
| `auth.refreshToken.invalid` | 401 — unknown, expired, revoked or replayed |
| `auth.externalSignIn.refused` | 401 — one answer for an unknown provider and a token that did not verify |
| `auth.register.unavailable` | 409 |
| `auth.confirmEmail.invalid` / `auth.resetPassword.invalid` / `auth.changeEmail.invalid` | 400 — the token is unknown, spent or expired |
| `auth.twoFactor.alreadyEnabled` | 409 |
| `auth.account.notFound` | 404 — an administration action naming an account that does not exist |
| `auth.account.cannotDeleteSelf` / `auth.lockout.cannotTargetSelf` / `auth.roles.cannotTargetSelf` / `auth.twoFactor.cannotTargetSelf` | 403 — an administrator may not aim these at their own account |
| `auth.account.deletionRejected` / `auth.lockout.rejected` / `auth.twoFactor.administrativeDisableRejected` | 409 — the store refused the change |

**Resources**

| `code` | Status |
|---|---|
| `todoList.notFound` / `todoItem.notFound` / `storedFile.notFound` / `reminder.notFound` | 404 — a resource owned by somebody else is also 404, so ids cannot be enumerated |
| `reminder.targetNotFound` | 404 — the item a reminder is about does not exist or is not the caller's |
| `storedFile.notAvailable` / `storedFile.quarantined` | 409 — the content has no verdict yet, or was examined and refused. The second never becomes available |
| `storedFile.depositMissing` | 409 — `confirm` found nothing deposited against the registration |
| `storedFile.quotaExceeded` | 409 — the caller's own pending, file-count or byte allowance |
| `domain.invariantViolated` | 409 via `DomainGuard`; 400 if a use case's own catch is missing and `DomainException` reaches `GlobalExceptionHandler` |

**The request itself**

| `code` | Status |
|---|---|
| `request.validationFailed` | 400 — a body or query value the API refused on the way in, with the offending fields in `errors`. Model binding and body validation share this code deliberately |
| `paging.invalid` / `sort.invalid` / `filter.invalid` / `cursor.invalid` | 400 — one bound of the collection contract, named so a client knows which rule it broke |
| `precondition.failed` | 412 — the `If-Match` a write named is stale, unrecognised, or `*` against a missing/foreign resource |
| `precondition.required` | 428 — only when `Concurrency:IfMatch` is `Required`; the write named no version at all |
| `precondition.malformed` | 400 — `If-Match` is present but is neither `*` nor a comma-separated list of quoted entity tags |
| `idempotency.keyInvalid` | 400 — the `Idempotency-Key` header is blank or over 128 characters |
| `idempotency.keyReused` | 409 — the same key with a different body |
| `idempotency.inProgress` | 409 — the first request under this key has not finished |
| `idempotency.notReplayable` | 409 — the stored response cannot be replayed |
| `rateLimit.exceeded` | 429, with a `Retry-After` header |
| `request.malformed` | 400 — a rejection from the framework or middleware that carries no more specific code |
| `request.failed` | The fallback, for a status none of the above names. Seeing it means a producer answered a status this table has no entry for |
| `request.methodNotAllowed` | 405 |
| `request.notAcceptable` | 406 |
| `request.tooLarge` | 413 — over `RequestLimits:MaxRequestBodyBytes`, refused before the body is buffered |
| `request.unsupportedMediaType` | 415 |
| `request.timeout` / `request.cancelled` | 408 / 499 |
| `route.notFound` | 404 — an authenticated caller on a route that matched nothing; an anonymous one gets `auth.required` instead |
| `server.unexpected` | 500 |

A 500 carries no exception text — only a sanitised message and a `traceId` that
correlates with the full stack trace in the logs.

Every error response is served as `application/problem+json`.

### Rate limits

| Scope | Limit | On rejection |
|---|---|---|
| `api/v1/auth/*` | 10 requests/minute per IP | 429 + `Retry-After: 60` + `code: rateLimit.exceeded` |
| Everything else | 300 requests/minute per IP | same |

Fixed windows, partitioned by `RemoteIpAddress`. **Behind a reverse proxy this needs the
`ReverseProxy` section turned on**, otherwise every request appears to come from the proxy
and the whole world shares one partition. It is off by default because the trust list
depends on your topology — and the validator refuses to start with `Enabled: true` and both
lists empty, which would accept a forged `X-Forwarded-For` from anybody. The counters are
also in-process, so the limit a caller meets is the number above times your replica count.
Both points are argued in full in
[docs/CONFIGURATION.md](docs/CONFIGURATION.md#reverseproxy).

## Configuration

**Every key, its type, its default, its validated range and what happens when it is wrong:
[docs/CONFIGURATION.md](docs/CONFIGURATION.md).** That document is the single statement of
each of them; nothing here repeats a value it can point at.

Configuration is layered `appsettings.json` → `appsettings.Development.json` → user secrets
→ environment variables, and every section but `Cors` binds to an options class whose
validator is registered with `.ValidateOnStart()`. A missing or out-of-range value fails
the host at startup, in one pass, with a message naming the exact key — blanking
`EmailConfirmation:ConfirmEmailUrl` and shortening `Jwt:Key` produces
`'Jwt:Key' must be at least 32 bytes long to sign HS256 tokens.` and
`'EmailConfirmation:ConfirmEmailUrl' is required.` before Kestrel binds a port.

`appsettings.json` is tracked and holds **no secrets** — every secret-shaped value in it is
an empty string, which is why that file alone will not boot the app. The blanks are filled
from user secrets locally and from environment variables in a deployment.

One consequence is worth knowing before you point this at a mail relay: startup validation
rejects every SMTP mode that can end up sending in the clear against a non-loopback host,
`Auto` included, unless `Email:AllowInsecureTransport` says so outright. A containerised
sink such as mailpit needs both switches, and `docker-compose.yml` and `.env.example`
already set them — see
[docs/CONFIGURATION.md](docs/CONFIGURATION.md#email) for the three valid shapes.

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

Every test project is listed in `AppTemplate.sln`, so `dotnet test AppTemplate.sln` runs all of
them. Keep it that way: a project on disk but absent from the solution is skipped silently,
and CI asserts against exactly that (see the "Assert test projects exist and are in the
solution" step in `.github/workflows/ci.yml`) — a count stated here would be one more thing to
keep in step, so it is deliberately not stated.

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
`CONTRIBUTING.md`.

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

The features still separate themselves by **schema** — `identity`, `todo`, `reminders`,
`files`, and `platform` for the tables that belong to no feature — declared table by table
in each feature's `IEntityTypeConfiguration`. The single `__EFMigrationsHistory` sits in
`public`, because it belongs to none of them.

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
docker-compose.yml             db + mailpit + minio + api + worker
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
                                      mapping, repositories, queries and stores
                                      -> Application
    AppTemplate.Infrastructure.Identity/       ASP.NET Identity policy, JWT, refresh-token rotation
                                      (no database of its own)
                                      -> Application + Persistence
    AppTemplate.Infrastructure.Email/          MailKit SMTP sender, email options
                                      -> Application
    AppTemplate.Infrastructure.Storage/        S3-compatible object store: signed grants, inventory
                                      -> Application
    AppTemplate.Infrastructure.InMemory/       in-memory port implementations for tests/demo
                                      -> Application
  Presentation/
    AppTemplate.Api/                           controllers, composition root, Dockerfile
                                      -> Application + every module
    AppTemplate.Worker/                        three BackgroundServices: maintenance, due reminders, file sweeps
                                      -> Application + Persistence + Identity + Email + Storage

Tests/
  Domain/AppTemplate.Domain.UnitTests/           the aggregate, in memory
  Application/AppTemplate.Application.UnitTests/ use cases against test doubles
  Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/
                                        the domain <-> row mapper, reflection-driven
  Infrastructure/AppTemplate.Infrastructure.Identity.UnitTests/  the authentication adapters
  Infrastructure/AppTemplate.Infrastructure.Email.UnitTests/     the MailKit sender, in isolation
  Infrastructure/AppTemplate.Infrastructure.Storage.UnitTests/   the S3 adapters, without a network
  Infrastructure/AppTemplate.Infrastructure.InMemory.UnitTests/  the test/demo doubles themselves
  Presentation/AppTemplate.Api.UnitTests/        controllers and request/response mapping
  Presentation/AppTemplate.Worker.UnitTests/     the three loops, their options and their resilience
  Architecture/AppTemplate.Architecture.Tests/   layer/module rules + container composition
  Integration/AppTemplate.Api.IntegrationTests/  the real host over HTTP, real PostgreSQL
  Integration/AppTemplate.Infrastructure.Identity.IntegrationTests/
                                        the refresh-token rotation race, two concurrent
                                        AppDbContext instances against real PostgreSQL

docs/                          ADDING-A-FEATURE.md, ARCHITECTURE.md, CONFIGURATION.md,
                               DEPLOYMENT.md, REMOVING-THE-EXAMPLE-FEATURES.md

deploy/
  kubernetes/                  Deployment, Service, Ingress, migration Job — see docs/DEPLOYMENT.md
```

The directory under `Src/` names the **layer**; the project inside it keeps its own name
and its own root namespace. Moving a project between layer folders therefore changes no
namespace — the disk path and the root namespace are independent.

### Inside a project: feature first, responsibility second

Within `AppTemplate.Application` and `AppTemplate.Api`, the top-level partition is the **business feature**,
and only inside a feature is code grouped by what it does:

```
AppTemplate.Application/
  Common/                       Results/ (Result, Error, ErrorType, PagedResult),
                                 Abstractions/ (cross-feature ports), Validation/,
                                 Idempotency/, Collections/, Concurrency/ — one folder per
                                 subject, nothing loose at the root
  Features/
    TodoLists/
      Errors/                   TodoListErrors.cs — the feature's failure vocabulary
      Policies/                 TodoListCollectionPolicy — the sortable whitelist
      Ports/TodoListQueries/    ITodoListQueries, TodoListFilter, TodoListPageRequest
      Services/                 ITodoListService — the one gate every command loads its aggregate through
      Extensions/               TodoListItemExtensions — a known-item id turned into the same 404 everywhere
      Mapping/                  TodoListDtoMapping — the aggregate a write just staged, read back as a DTO
      Consumers/TodoItemCompleted/  a worked example of a domain-event consumer
      UseCases/Commands/<Operation>/   CreateTodoList, RenameTodoList, DeleteTodoList, AddTodoItem,
                                UpdateTodoItem, RemoveTodoItem, CompleteTodoItem, ReopenTodoItem,
                                AddTagToTodoItem, RemoveTagFromTodoItem, ReplaceTodoItemTags — one
                                folder per operation, each holding its command, named interface, use
                                case and validator
      UseCases/Queries/<Operation>/    GetTodoLists, GetTodoList, GetTodoItems, GetTodoItem
      Dtos/                     TodoListSummaryDto, TodoListDetailDto, TodoItemDto — read models more
                                than one operation returns
    Reminders/                  the second worked example: a flat aggregate, no child entities
      Errors/                   ReminderErrors.cs
      Ports/<Port>/             ReminderNotifier, ReminderTargetQueries (is the target still
                                outstanding?), ReminderDiagnostics (the missed-cancellation counter)
      Services/                 IReminderService — identity, ownership and precondition in one gate
      Mapping/                  ReminderDtoMapping
      Consumers/TodoItemCompleted/  cancels an item's reminders — a fast path, not the guarantee
      UseCases/Commands/<Operation>/   ScheduleReminder, RescheduleReminder, CancelReminder, and
                                FireDueReminders, which the worker runs and which re-reads its
                                target rather than trusting the event that should have cancelled it
      UseCases/Queries/<Operation>/    GetReminders
      Dtos/                     ReminderDto
    Auth/
      Errors/                   AuthErrors.cs — the vertical's failure vocabulary
      Policies/                 CredentialInvalidationPolicy, PasswordPolicy,
                                SelfAdministrationPolicy
      Ports/<Port>/             UserAccounts, EmailConfirmationTokens, AccessTokenIssuer,
                                RefreshTokenGrants, RefreshTokenMaintenance, ConfirmationEmailFactory,
                                PasswordResetTokens, PasswordResetEmailFactory, SecurityEventLog,
                                UserProfiles, and others — one port per capability, in place of
                                one IAuthService
      UseCases/Commands/<Operation>/   Register, Login, Logout, LogoutEverywhere,
                                RefreshAccessToken, ConfirmEmail, ResendConfirmationEmail,
                                ChangePassword, RequestPasswordReset, ResetPassword
      UseCases/Queries/GetCurrentUser/
    Files/                      the third worked example: one aggregate whose two halves live
                                in different stores — metadata in PostgreSQL, bytes behind a port
      Errors/                   StoredFileErrors.cs
      Policies/                 StoredFileCollectionPolicy (the sortable whitelist),
                                StoredFileQuotaPolicy (what one owner may hold),
                                StoredFileContentPolicy + MediaTypeSignatures (what the leading
                                bytes are allowed to say)
      Ports/<Port>/             StoredFileQueries, FileContentStore (signed grants),
                                FileContentInventory (what the bucket holds),
                                FileContentInspector — four ports where TodoLists has one,
                                because the bytes are somebody else's store
      Services/                 IStoredFileService — the one gate every command loads through
      Mapping/                  StoredFileDtoMapping
      Consumers/StoredFileDeleted/  reclaims the object promptly; the sweep is the guarantee
      UseCases/Commands/<Operation>/   RegisterFile, ConfirmFileUpload, DeleteStoredFile, and the
                                three the worker runs: InspectDepositedFiles,
                                PurgeAbandonedRegistrations, ReclaimOrphanedContent
      UseCases/Queries/<Operation>/    GetStoredFiles, GetStoredFile, IssueFileDownload
      Dtos/                     StoredFileDto
    Maintenance/                no aggregate and no domain of its own: two commands over rows
      UseCases/Commands/<Operation>/   PurgeExpiredIdempotencyKeys, PurgeExpiredRefreshTokens
```

A command or query record lives **in the same folder as the one use case that accepts
it**, alongside that use case's named interface, its class and its FluentValidation
validator, because together they are that operation's signature. A response type only
that one operation returns stays there too; a read model more than one operation
shares is promoted to `Dtos/`, and a type that is a *port's* own parameter — not one
use case's — lives beside that port in `Ports/<Port>/` instead, however many use cases
call it.

In `AppTemplate.Domain`, `AppTemplate.Application` and `AppTemplate.Api` there is deliberately **no `Services/`,
`Interfaces/`, `DTOs/`, `Helpers/`, `Managers/` or `Factories/` folder at the project
root.** Grouping by technical type at the top level puts the six files that implement one
feature in six different directories: no change is ever local, and no folder tells you what
the application does. A responsibility folder is legitimate only *inside* a feature, where
it partitions something that is already cohesive.

**Every infrastructure module is partitioned `Common/` plus `Features/<Feature>/`**, whether
or not it serves more than one feature — a reader who has learned one module does not learn
a second filing system for the next. `Email` and `InMemory` carry both kinds of adapter, a
transverse `IEmailSender` and a feature-scoped `IReminderNotifier`, so their tree says which
of their files leave with the reminders example. `Identity` and `Storage` each serve one
feature and keep the same shape anyway: `Identity` is `Common/{Directories,Options}` plus
`Features/Auth/{Directories,Factories,Issuers,Logs,Options,Providers,Services,Templates,Verifiers}`,
`Storage` is `Common/{Budgets,Factories,Options}` plus `Features/Files/`. Inside a feature
the folders name responsibilities, exactly as they do in the inner layers.

`AppTemplate.Infrastructure.Persistence` holds more than one capability, so it is partitioned the same
way the inner layers are — feature first:

```
AppTemplate.Infrastructure.Persistence/
  Common/                       the cross-cutting mechanisms, which name no feature
    Contexts/                   AppDbContext (the model's composition root), design-time
                                factory, connection-string helper
    Saving/                     everything one SaveChangesAsync does: EfUnitOfWork and the
                                EF -> ConcurrencyConflictException translation, then
      Auditing/                 the audit interceptor
      DomainEvents/             dispatcher, dispatch interceptor, consumer + source contracts
      Tracking/                 the shared tracker core, IAggregateFlusher, the flush
                                interceptor and the stored audit stamps
    Idempotency/                the idempotency-key record, its store, its EF configuration
                                and IdempotencyPurgeOptions
    Leases/                     PostgresLeaderLease — the advisory lock that makes the worker
                                replicable
    Options/                    DatabaseOptions and its validator
    Time/                       the system clock
  Features/
    TodoLists/
      Models/                   TodoListRecord, TodoItemRecord, TodoItemTagRecord
      Configurations/           IEntityTypeConfiguration for each record
      Mapping/                  ITodoListMapper: aggregate <-> rows
      Tracking/                 the per-request identity map, flusher and event source
      Repositories/             TodoListRepository : ITodoListRepository
      Queries/                  TodoListQueries : ITodoListQueries (rows -> DTOs, in SQL)
    Reminders/                  the same six, plus
      Observability/            ReminderDiagnostics — the only feature-local Observability/,
                                and the reason is below
    Files/                      Models, Configurations, Mapping, Tracking, Repositories,
                                Queries — an aggregate half of whose data is in another store,
                                so the queries answer about rows and never about bytes
    Identity/
      Models/                   AppUser, AppRole, RefreshToken
      Configurations/           table and index mapping, one schema per feature
      Tables/                   IRefreshTokenTable — rows in one table, not an aggregate repository
      Seeding/                  IIdentitySeeder and its options
  Migrations/                   one history
  PersistenceModule.cs
```

The `TodoLists` feature's own domain-event consumer is not here: publishing an event is a
persistence mechanism, but deciding what happens next is application behaviour, so
`LogTodoItemCompletedConsumer` lives in `AppTemplate.Application/Features/TodoLists/Consumers/`
instead, registered from `ApplicationModule`, not `PersistenceModule`.

An architecture test asserts the rule that layout encodes: nothing under this project's
`Common/` may depend on a feature's domain or persistence types. `AppDbContext` is the one
documented exception — it applies every feature's configuration, which is what makes it the
model's composition root. The rule checks type dependencies rather than identifiers, so a
file merely *named* after a feature would not trip it; `ReminderDiagnostics` sits in
`Features/Reminders/Observability/` because that is where layout puts it, not because the
test forced it there.

The word "Module" is kept for exactly one thing: dependency-injection registration classes
— `ApplicationModule`, `PersistenceModule`, `IdentityModule`, `EmailModule`, `StorageModule`
and `InMemoryModule`. That is a composition concept, not a business partition.

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

An `EXPOSE 8081` with no certificate provisioned, no `ASPNETCORE_HTTPS_PORTS` set and
nothing bound to the port is a published port that cannot answer. Half-configured TLS
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

> The Dockerfile's `COPY` list names each project file individually. **Adding a project
> reference to `AppTemplate.Api` means adding it here too**, or restore inside the image
> will fail on a missing `.csproj` even though the local build is fine. The `docker` job
> in CI is what catches that.

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
fails against a Coverlet-instrumented assembly. Under the collector some of its rules throw; without
it they all pass. Every test still runs exactly once. Do not merge those two steps back together.

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
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | running it on Kubernetes: the raw manifests in `deploy/kubernetes/`, and why their numbers are what they are |
| [AppTemplate.Api.http](AppTemplate.Api.http) | the whole API walkthrough, executable |
| [Tools/](Tools/) | the six single-file C# apps this repository runs on itself — detailed below |

If you read only one of them before deploying, read `SECURITY.md`: its second section is longer than
its first, and that is the honest shape of the thing.

### `Tools/`

Six single-file C# apps, each launched with `dotnet run <file>`, each needing nothing beyond
the SDK `global.json` pins — no interpreter to find, no package to install, and the same line
typed on Windows, Linux and macOS.

| File | What it is |
|---|---|
| [Tools/Tasks.cs](Tools/Tasks.cs) | the task launcher: thin wrappers over the real `dotnet` and `docker` commands |
| [Tools/CheckDocPaths.cs](Tools/CheckDocPaths.cs) | every repository path cited in a Markdown code span resolves on disk |
| [Tools/CheckWorkflows.cs](Tools/CheckWorkflows.cs) | what a YAML parse does not catch: a dangling `needs:`, an action on a mutable tag, a missing `permissions:`, a `$VAR` in no `env:`, a script named in a `run:` and absent from disk |
| [Tools/CoverageGate.cs](Tools/CoverageGate.cs) | the Cobertura reports of a test run against the floor in `coverage.minimum` |
| [Tools/CheckNarrativeComments.cs](Tools/CheckNarrativeComments.cs) | no comment narrates the repository's own history, across `.cs` and `.md` alike |
| [Tools/TestSummary.cs](Tools/TestSummary.cs) | sums the TRX counters into the job summary, and fails a run that executed no test |

The four gates each take a `--self-test` flag that runs them against faulted and sound
fixtures, so a green is contrasted with a red before any of them judges the repository.

`Tools/Tasks.cs` prints each command before running it, which is what keeps the launcher
honest: copy the printed line and you get the same result without it. No task hides a flag
that changes the meaning of a build — in particular none relaxes `TreatWarningsAsErrors`.

## Licence

MIT — see [LICENSE](LICENSE).
