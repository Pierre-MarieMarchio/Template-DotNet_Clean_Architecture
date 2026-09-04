# Configuration

Every setting the application reads is listed here. The tracked
`Src/Presentation/AppTemplate.Api/appsettings.json` is the schema of record and contains **no secrets** —
every secret-shaped value in it is an empty string.

## How configuration is loaded

Sources, later overriding earlier:

1. `appsettings.json` — committed, non-secret, complete key list.
2. `appsettings.Development.json` — committed, localhost throwaways. Loaded **only**
   when `ASPNETCORE_ENVIRONMENT=Development`, and **only** under that exact spelling.
3. **User secrets** — local machine, never committed. `UserSecretsId` is already set
   in `AppTemplate.Api.csproj`.
4. **Environment variables** — how deployed environments and `docker compose` supply
   values. Nest sections with a double underscore: `Jwt:Key` becomes `Jwt__Key`,
   `ConnectionStrings:Default` becomes `ConnectionStrings__Default`.

## Validation happens at startup, not on first use

Each section below binds to an options class with an `IValidateOptions<T>` validator
registered with `.ValidateOnStart()`. A missing or out-of-range value **fails the host
at startup** with a message naming the exact key.

Verified — blanking `EmailConfirmation:ConfirmEmailUrl` and shortening `Jwt:Key`:

```
Hosting failed to start
System.AggregateException: One or more errors occurred.
  ('Jwt:Key' must be at least 32 bytes long to sign HS256 tokens.)
  ('EmailConfirmation:ConfirmEmailUrl' is required.)
```

Every failing key is reported in one pass, before Kestrel binds a port. An empty
string in `appsettings.json` therefore behaves as "you must supply this", not as a
value that silently works.

This means `appsettings.json` alone will not boot the app — by design. The blanks must
be filled from user secrets or environment variables.

> **Where the options classes live.** Each section is bound and validated by the module
> that consumes it, so the file path follows the responsibility rather than the history:
>
> | Section | Bound by | Declared in |
> |---|---|---|
> | `Jwt`, `Identity`, `RefreshToken`, `EmailConfirmation` | `AddIdentityModule` | beside the service each configures, under `AppTemplate.Infrastructure.Identity/<Subject>/` |
> | `IdentitySeed` | `AddPersistenceModule` | `AppTemplate.Infrastructure.Persistence/Features/Identity/Seeding/` |
> | `Email` | `AddEmailModule` | `AppTemplate.Infrastructure.Email/Options/` |
> | `Database` | `AddPersistenceModule` | `AppTemplate.Infrastructure.Persistence/PersistenceModule.cs` |
> | `IdempotencyPurge` | `AddPersistenceModule` | `AppTemplate.Infrastructure.Persistence/Common/Idempotency/IdempotencyStore.cs` |
> | `MaintenanceWorker` | `AppTemplate.Worker`'s own `Program.cs` | `AppTemplate.Worker/Features/Maintenance/` |
>
> `IdentitySeed` sits with the seeder because seeding is a persistence concern, not an
> authentication policy. **The configuration keys and their validation do not change** —
> only the file paths and which DI extension method binds them. Treat any path in this
> document as indicative and the key names as authoritative.

## Two hosts, one configuration schema

`AppTemplate.Worker` (`Src/Presentation/AppTemplate.Worker`) is a second entry point that calls
`IPurgeExpiredIdempotencyKeysUseCase` and `IPurgeExpiredRefreshTokensUseCase` — the exact same
application-layer use cases `MaintenanceController` exposes over HTTP — on a timer instead of a
request, and rings a due reminder by mail through the exact same `IReminderNotifier` port the API
would use if it ever called it. It composes `AddApplicationLayer`, `AddPersistenceModule`,
`AddIdentityModule` **and** `AddEmailModule`, so it reads `ConnectionStrings`, `Database`,
`IdempotencyPurge`, `Jwt`, `RefreshToken`, `EmailConfirmation`, `PasswordReset`, `EmailChange` and
`Email` exactly like the API, plus its own `MaintenanceWorker` section below. `IdentitySeed` is
bound and validated too — `AddPersistenceModule` does that unconditionally — but every one of its
members has a safe default (`Enabled: false`), so an absent section validates cleanly; the worker
never *exercises* seeding either way, since `IIdentitySeeder`/`MigrateAndSeedForDevelopmentAsync`
are only ever called from `AppTemplate.Api/Program.cs`. The worker does **not** read `Cors`,
`ReverseProxy`, `SecurityHeaders`, `OpenTelemetry`, `Concurrency`, `Idempotency`, `RequestLimits`,
`Shutdown` or `RequestTimeouts` — those are the API's transport-layer concerns, and the worker has
no transport layer. The worker still waits for its own in-flight iteration to finish on shutdown,
on `HostOptions.ShutdownTimeout`'s framework default — nothing here raises it for that host.

**Why the worker validates `Jwt`, `Identity`, `RefreshToken`, `EmailConfirmation`, `PasswordReset`
and `EmailChange` at startup even though it never authenticates anybody.**
Two reasons, and for a long time this document named only the smaller of them.

The smaller: `IRefreshTokenMaintenanceService`'s only adapter lives in
`AppTemplate.Infrastructure.Identity`. Read alone, that invites an obvious conclusion — move that
one adapter into the persistence project, where the `IRefreshTokenTable` it drives already lives,
and the worker sheds six configuration sections. **That conclusion is wrong**, which is worth
saying plainly because it is the first thing anyone reading this reaches for.

The one that decides it: `EmailReminderNotifier` resolves `IUserProfilesService` — an identity
port — to find the address a due reminder is rung at. That is the reminder loop, which is this
host's own feature and its main reason to exist. So the worker needs the identity module whatever
happens to the maintenance adapter.

And above both: `AddApplicationLayer` registers *every* use case in the assembly, and
`Host.CreateApplicationBuilder` turns on `ValidateOnBuild` in Development. Every port the
application layer declares therefore has to be resolvable in every host — not only the ports that
host's own loops reach. A worker composed without the identity module fails to build its container
naming twenty-odd unresolvable use cases, not one.
`TheWorkerContainer_NeedsIdentityForItsReminderLoop_NotOnlyForThePurgeAdapter` holds all of this,
so the paragraph cannot drift back to the convenient version.

Composing that module also composes ASP.NET Identity, bearer validation and its own configuration
surface as a whole — there is no narrower call that gets only one adapter. This is a
real coupling cost of the current module boundary, not an oversight.

**One consequence is worth separating from the rest: the worker needs the `Jwt` section, not the
`Jwt:Key` value.** `AccessTokenIssuer` is the only thing that signs with it, no loop in this host
resolves it, and bearer validation needs an inbound request this process does not have — so
`docker-compose.yml` and `deploy/kubernetes/configmap-worker.yaml` give it a fixed, self-describing
placeholder, and only the API's Deployment references the real key. `Jwt:Issuer` and `Jwt:Audience`
are kept identical between the two, because those are what the hosts have to agree about. See
`SECURITY.md`.

The worker's `appsettings.json`
therefore carries the same required `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`,
`EmailConfirmation:ConfirmEmailUrl`, `PasswordReset:ResetPasswordUrl` and
`EmailChange:ConfirmEmailChangeUrl` as the API, and its generic host reads `DOTNET_ENVIRONMENT`
(not `ASPNETCORE_ENVIRONMENT` — there is no ASP.NET Core host here) to select
`appsettings.Development.json`.

**Why the worker also validates `Email` (SMTP) at startup, even though it renders no confirmation
mail of its own.** Same shape of cost, one module over: `IReminderNotifier`'s only adapter lives
in `AppTemplate.Infrastructure.Email`, and there is no way to compose that adapter without also
composing `EmailOptions` and its validator — `AddEmailModule` does not offer a narrower call
either. This is real, not incidental: a deployment that never enables reminders still has to point
this host at a working SMTP relay (`Email:Host`, `Email:FromAddress`, …) for the process to start
at all. See `AppTemplate.Worker.csproj` for the same note next to the `AddEmailModule` reference.
No mechanism in this template lets a host opt out of an options section a composed module always
validates; if that stops being acceptable, the fix is a narrower module boundary — a package that
exposes only `IReminderNotifier`'s adapter without the rest of `AppTemplate.Infrastructure.Email`'s
surface — not a per-host configuration override.

**`ICurrentUser` outside a request.** The API's `CurrentUser` reads `IHttpContextAccessor`, which
the worker cannot depend on and would be `null` for every call anyway — silently producing a
`UserId` of `null` indistinguishable from a legitimate anonymous HTTP request. The worker instead
registers `BackgroundCurrentUser`, whose `UserId` getter throws `NotSupportedException`: a use case
moved onto this host that reads the caller's identity must fail loudly at that call, not proceed
as if it were an anonymous request.

## Secrets: use `dotnet user-secrets`

`Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj` carries a `UserSecretsId`, so the store already exists.
Values live outside the repository (on Windows,
`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`) and override both appsettings
files.

```bash
cd Src/Presentation/AppTemplate.Api

# The two things you actually need to run locally
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=<your password>"
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"

dotnet user-secrets list      # show what is set
dotnet user-secrets remove "Jwt:Key"
dotnet user-secrets clear
```

Never put a secret in `appsettings.json` or `appsettings.Development.json` — both are
tracked in git.

## Reference

Types are the bound CLR types. "Required" means startup fails without it. Defaults are
the property initialisers on the options class, which apply when the key is absent —
they are **not** the values in `appsettings.Development.json`, which override them.

### `ConnectionStrings`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Default` | string | — | **Required.** The one and only connection string. Npgsql format. Every DbContext uses it; see [ARCHITECTURE.md](ARCHITECTURE.md#two-dbcontexts-one-database). |

Example: `Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=…`

A missing or blank value throws `InvalidOperationException` from the DI registration
itself — before options validation runs — with the message
`The 'Default' connection string is not configured.`

### `Database`

| Key | Type | Default | Notes |
|---|---|---|---|
| `MaxPoolSize` | int | `20` | Applied to `ConnectionStrings:Default` as Npgsql's `Maximum Pool Size`. Range **1–500**. |
| `CommandTimeoutSeconds` | int | `30` | Npgsql's per-command timeout, matching the driver's own default. Range **1–300**. |

**Why `MaxPoolSize` defaults to 20 and not Npgsql's own default of 100.** PostgreSQL's
`max_connections` also defaults to 100 **for the whole server**, not per client. Two replicas of
this process at the driver default (100 each) are already enough to exhaust it before anything
else — a monitoring dashboard, a `psql` session, a second application — gets a connection at all.
20 is deliberately conservative so that running several replicas, of the API *and* the worker,
against one PostgreSQL instance does not by itself approach the server's ceiling.

**Sizing this against replica count and `max_connections`.** Every replica of every process that
calls `AddPersistenceModule` — each API instance and each worker instance — holds its own pool up
to `MaxPoolSize`. Budget:

```
sum over every replica of (that replica's Database:MaxPoolSize)
  + PostgreSQL's own reserved connections (superuser_reserved_connections, default 3)
  <= max_connections
```

With the shipped default of 20 and `max_connections=100`, that is room for **4–5 replicas total**
(API and worker combined) before the server itself starts refusing connections — and refusing a
connection is not a graceful degradation, it is a hard failure of every feature that touches the
database at once. Raising `max_connections` costs PostgreSQL memory per slot; lowering
`MaxPoolSize` costs this process concurrency headroom under its own load. Pick the one that is
cheaper for your deployment, but pick one — the failure mode of picking neither is silent until
the day a second replica starts.

**The idempotency store's extra connections count against this same budget, not a separate one.**
`IIdempotencyStore` is backed by `IDbContextFactory<AppDbContext>` rather than the request's
ambient `AppDbContext` — see the XML doc on `IdempotencyStore` for why that has to be a second,
independently-committed connection. In practice that means **one idempotent write can hold up to
three connections from this same pool at once**: the ambient request connection, the factory
connection the claim is written through, and the factory connection the claim is completed
through. A capacity plan that only counts "one connection per request" under-provisions for every
idempotent endpoint. `AddContextFactory` deliberately shares `AddContext`'s options object rather
than building a second one, specifically so both draw from the one pool this setting bounds
instead of a second, unbounded one.

**The worker is a replica of the same budget, not a bystander.** `AppTemplate.Worker` composes
`AddPersistenceModule` too, and its own `Database:MaxPoolSize` — independently configurable —
holds connections against the same server. Size it in, not around.

### `IdempotencyPurge`

| Key | Type | Default | Notes |
|---|---|---|---|
| `BatchSize` | int | `1000` | Rows deleted per round trip by the expired-key purge. Range **1–100000**. |

Read by `IdempotencyStore.PurgeExpiredAsync`. Under sustained ingestion the expired range can be
hundreds of thousands of rows; deleting it in one `DELETE` holds one lock for the whole scan and
produces one large burst of dead-tuple bloat. This purges in a loop of bounded batches instead,
ordered by the already-indexed `ExpiresAt` column, and logs the total once the sweep finishes.
Smaller batches mean more round trips and a shorter lock per trip; larger batches mean the
opposite. 1000 is a starting point, not a measured optimum for your ingestion rate.

### `Jwt`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Key` | string | `""` | **Required.** HS256 signing key, **minimum 32 bytes**. Secret — user secrets or env var only. |
| `Issuer` | string | `""` | **Required.** Issuer validation is never disabled. |
| `Audience` | string | `""` | **Required.** Audience validation is never disabled. |
| `RequireHttpsMetadata` | bool | `true` | Only a local host has business setting `false`. |
| `AccessTokenLifetimeInMinutes` | int | `15` | Range **1–1440**. Short on purpose: an access token cannot be revoked. |

### `RefreshToken`

| Key | Type | Default | Notes |
|---|---|---|---|
| `LifetimeInDays` | int | `7` | Range **1–90**. Token size (32 bytes) and hashing (SHA-256) are not configurable. |

### `Identity`

Password, lockout and sign-in policy. Every member has a safe default, so a
partly-filled section neither tightens nor loosens anything unexpectedly.

| Key | Type | Default | Notes |
|---|---|---|---|
| `PasswordRequiredLength` | int | `12` | **Hard floor of 8** — lower values are rejected, and the value is also clamped before reaching ASP.NET Identity. Max 256. |
| `PasswordRequiredUniqueChars` | int | `4` | Minimum 1. |
| `PasswordRequireDigit` | bool | `true` | |
| `PasswordRequireLowercase` | bool | `true` | |
| `PasswordRequireUppercase` | bool | `true` | |
| `PasswordRequireNonAlphanumeric` | bool | `true` | |
| `LockoutEnabled` | bool | `true` | This is what bounds online password guessing. Maps to `Lockout.AllowedForNewUsers`. |
| `LockoutMaxFailedAccessAttempts` | int | `5` | Range 1–20. |
| `LockoutDurationInMinutes` | int | `15` | Minimum 1. |
| `RequireConfirmedEmail` | bool | `true` | With this on, an unconfirmed account cannot log in — and the failure is indistinguishable from a wrong password (`auth.login.invalidCredentials`). |
| `RequireUniqueEmail` | bool | `true` | **Cannot be `false`** — the user table has a unique index on the normalised email. |

### `Email`

SMTP transport.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Host` | string | `""` | **Required.** |
| `Port` | int | `587` | Range 1–65535. |
| `Security` | enum | `StartTls` | MailKit `SecureSocketOptions`: `None`, `Auto`, `SslOnConnect`, `StartTls`, `StartTlsWhenAvailable`. Constrained — see below. |
| `AllowInsecureTransport` | bool | `false` | Explicit opt-in to an unencrypted transport against a **non-loopback** host. |
| `UserName` | string? | `null` | Optional — some relays authenticate by IP rather than credentials. |
| `Password` | string? | `null` | Secret. |
| `FromAddress` | string | `""` | **Required.** Must parse as a mailbox address. |
| `FromName` | string | `""` | |

**How `Security` is constrained.** Startup validation rejects every mode that can end
up sending in the clear — `None`, `StartTlsWhenAvailable` **and `Auto`** — when `Host`
is not a loopback address (`localhost`, `127.0.0.1`, `::1`, `[::1]`), unless
`AllowInsecureTransport` is `true`.

`Auto` is on that list deliberately: MailKit resolves it to `StartTlsWhenAvailable` on
any port other than 465, so allowing it would reopen exactly the downgrade the other
two are rejected for, under a friendlier name. That gap is how a development compose
file ended up with opportunistic TLS.

So there are three valid shapes:

| Situation | Setting |
|---|---|
| Real relay | `Security=StartTls` or `SslOnConnect`, `AllowInsecureTransport=false` |
| Loopback sink on the host (`localhost:1025`) | `Security=None`, no opt-in needed |
| Containerised sink (`mailpit:1025` — not loopback, no TLS) | `Security=None` **and** `AllowInsecureTransport=true` |

`appsettings.Development.json` uses the second; `docker-compose.yml` and
`.env.example` use the third. Making the insecure transport a separate, explicitly
named boolean is the point: it is auditable in configuration and in a diff, whereas
reaching for a permissive `Security` mode is not.

### `EmailConfirmation`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConfirmEmailUrl` | URI | — | **Required.** Absolute `http`/`https` URL of the page that completes confirmation, with **no fragment**. Must be browser-reachable, not a container name. |
| `Subject` | string | `Confirm your email address` | Must not be blank. |

The email address and single-use token are appended as a URL **fragment**, which
browsers never transmit — so the token stays out of server access logs, `Referer`
headers and intermediary request history. The page reads the fragment and **POSTs** it
to `/api/v1/auth/confirm-email` as a JSON body. Confirmation is not a GET with a query
string, for the same reason.

### `PasswordReset`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ResetPasswordUrl` | URI | — | **Required.** Absolute `http`/`https` URL of the page that completes the reset, with **no fragment**. Must be browser-reachable, not a container name. |
| `Subject` | string | `Reset your password` | Must not be blank. |
| `TokenLifespan` | timespan | `01:00:00` | Must be between **5 minutes and 1 day**. Its own value, deliberately not shared with `EmailConfirmation`'s token lifespan — see the XML doc on `PasswordResetOptions` for why one shared lifespan across every token provider would be wrong here. |

Same fragment-then-POST shape as `EmailConfirmation`, and for the same reason: the email
and single-use token are appended as a URL fragment, and the page **POSTs** them to
`/api/v1/auth/reset-password` as a JSON body.

### `EmailChange`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConfirmEmailChangeUrl` | URI | — | **Required.** Absolute `http`/`https` URL of the page that completes the change, with **no fragment**. Must be browser-reachable, not a container name. |
| `Subject` | string | `Confirm your new email address` | Must not be blank. |
| `TokenLifespan` | timespan | `01:00:00` | Must be between **5 minutes and 1 day**. Its own named token provider, same reasoning as `PasswordReset:TokenLifespan` above. |

Same fragment-then-POST shape again: the new address and single-use token are appended
as a URL fragment, and the page **POSTs** them to `/api/v1/auth/confirm-email-change` as
a JSON body.

**Both hosts validate all three of `EmailConfirmation`, `PasswordReset` and `EmailChange` at
startup.** All three are bound by `AddIdentityModule`, not by anything HTTP-specific, so
`AppTemplate.Worker` — which composes that module for its reminder loop's `IUserProfilesService`
and for `IRefreshTokenMaintenanceService`'s adapter, see
[above](#two-hosts-one-configuration-schema) — requires all three URLs too, even though it never
serves any of these three requests itself.

### `IdentitySeed` — development only

Creates an administrator account at startup.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Opt-in. Additionally, `IdentitySeeder` **throws** if this is true outside the Development environment. |
| `AdminUserName` | string | `administrator` | Required when enabled. |
| `AdminEmail` | string | `""` | Required when enabled. |
| `AdminPassword` | string | `""` | **Required when enabled, and has no default.** Supply via user secrets or an env var. |

> **Warning.** Enabling this creates a privileged account with the `Admin` role and a
> pre-confirmed email address. It is off by default, refuses to run outside
> Development, and enabling it with a blank password **fails startup** rather than
> creating a guessable administrator. Never enable it in an environment reachable from
> the internet.

Note that seeding only runs from the Development startup path
(`MigrateAndSeedForDevelopmentAsync`), so setting `Enabled=true` in another
environment fails at that call site rather than being quietly ignored.

### `Cors`

| Key | Type | Default | Notes |
|---|---|---|---|
| `AllowedOrigins` | string[] | `[]` | Exact origins. Bind by index from the environment: `Cors__AllowedOrigins__0`, `…__1`, … |

Reconciled against `Src/Presentation/AppTemplate.Api/Common/Security/CorsExtensions.cs`, which reads the key
`Cors:AllowedOrigins`:

- **An empty or absent array allows nothing**, rather than silently allowing
  everything. A same-origin caller is unaffected, since CORS only governs
  cross-origin requests.
- When origins are configured, the policy allows any header and any method, exposes
  `Retry-After`, and caches preflight for 10 minutes.
- `AllowCredentials` is deliberately **not** set: tokens travel in the `Authorization`
  header, not a cookie. It is the wildcard-origin-plus-credentials combination that
  turns a permissive policy into a vulnerability.

`appsettings.Development.json` ships `http://localhost:4200` and
`http://localhost:5173`.

### `ReverseProxy`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Off means no forwarding header is trusted at all. |
| `KnownProxies` | string[] | `[]` | Literal addresses of the immediate hops, e.g. `10.0.0.7`. |
| `KnownNetworks` | string[] | `[]` | CIDR blocks, e.g. `10.0.0.0/8`. The address must be the network address: `10.0.0.1/8` is rejected, not silently masked. |
| `ForwardLimit` | int | `1` | How many entries to consume from the right of `X-Forwarded-For`. Must equal the number of proxies actually in front of the app. |

Read by `Src/Presentation/AppTemplate.Api/Common/Security/ForwardedHeadersExtensions.cs`. This is the
section that decides whether rate limiting works, so it is worth reading twice.

- **Leave it off and the rate limiter partitions on the proxy's address**, which means every
  caller on earth shares one 10-request window and the brute-force protection does nothing.
- **Turning it on with both lists empty is worse, not a shortcut.** ASP.NET Core only verifies
  the peer when at least one proxy or network is known; with both lists empty it accepts
  `X-Forwarded-For` from anybody, so a caller can forge its own partition key and bypass the
  limiter completely. The validator **refuses to start** in that state rather than letting it
  look configured.
- The framework seeds both lists with loopback; the template clears that. A loopback sidecar
  proxy must be listed explicitly like any other, because "shares the host" is not the same as
  "is trusted".
- `ForwardLimit` larger than the real hop count lets a caller prepend a forged hop and have it
  read as the client address.
- `X-Forwarded-Host` is deliberately **not** honoured — use `AllowedHosts` for that.

Only `X-Forwarded-For` and `X-Forwarded-Proto` are processed, and the middleware runs before
anything that reads the address or the scheme.

### `SecurityHeaders`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ContentSecurityPolicy` | string | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` | The policy sent on API responses. |

Read by `Src/Presentation/AppTemplate.Api/Common/Security/SecurityHeadersExtensions.cs`. Alongside the CSP,
every response also carries `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`
and `X-Frame-Options: DENY`; `Server` and `X-Powered-By` are suppressed.

Two details that are easy to undo by accident:

- Headers are written from a `Response.OnStarting` callback rather than set on the way in,
  because `UseExceptionHandler` calls `Response.Clear()` before re-running the pipeline. Set
  them eagerly and they vanish from exactly the 5xx ProblemDetails that most need them.
- **In Development, the Scalar page under `/scalar` gets a different, wider policy**, because
  `default-src 'none'` renders it blank. That branch is gated on both `IsDevelopment()` and the
  path prefix, and the API policy above is not weakened to accommodate it. Scalar's inline
  module script runs on a per-request nonce rather than `'unsafe-inline'`.

**The application sends no `Strict-Transport-Security` header.** An ingress
terminating TLS is required to send it.

### `OpenTelemetry`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Off registers no instrumentation, no exporter and no background flush. |
| `OtlpEndpoint` | string | `""` | e.g. `http://collector:4317`. |
| `OtlpProtocol` | string | `Grpc` | `Grpc` or `HttpProtobuf`. |
| `ServiceName` | string? | `null` | Falls back to the assembly name and informational version. |

Read by `Src/Presentation/AppTemplate.Api/Common/Observability/ObservabilityExtensions.cs`. Traces cover
ASP.NET Core, `HttpClient` and Npgsql; metrics cover ASP.NET Core and `HttpClient`. `/health*` is
excluded from both traces and the request log.

- **An unreachable collector is safe.** Startup succeeds and the failing export cycles produce
  no log output at all, because the OTLP exporter reports failures on its own `EventSource`
  rather than through `ILogger`. Verified against a dead endpoint.
- The `traceId` in a ProblemDetails body is `HttpContext.TraceIdentifier`. The request log
  records both that and the W3C `TraceId`, and the span is tagged with the `TraceIdentifier`, so
  a caller's `traceId` joins to the log entry and the log entry joins to the trace — from either
  end.
- **The request log never enumerates headers, reads cookies, touches the body, or logs the query
  string.** It writes a fixed field list. The auth endpoints carry passwords and refresh tokens
  in their bodies, so body logging is not merely disabled but absent. If you add logging of your
  own, this guarantee does not extend to it.

### `Concurrency`

| Key | Type | Default | Notes |
|---|---|---|---|
| `IfMatch` | `Optional` \| `Required` | `Optional` | `Required` refuses a mutating request with no `If-Match` header with `428`. |

Read by `Src/Presentation/AppTemplate.Api/Common/Concurrency/ConcurrencyExtensions.cs`. Every read of a
`TodoList` or `TodoItem` publishes a strong `ETag` regardless of this setting, and every write
already honours `If-Match` when one is sent — a stale, malformed or unrecognised version is
refused with `412` either way. This setting only decides what happens when a write names **no**
version at all.

- **Left at `Optional`, the template behaves exactly as it did before conditional requests
  existed**: an unconditional write still succeeds, guarded only by the `xmin` check inside the
  use case. This is deliberate, so that adding `If-Match` support does not
  silently start rejecting every client that predates it.
- **Set to `Required` only once every client of this deployment reads before it writes** and
  echoes back the `ETag` it read; otherwise every mutation starts answering `428`.
- Bound once at startup and validated: a value that is not one of the two enum members fails
  `ValidateOnStart()` rather than falling through to the more permissive branch.

### `Idempotency`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `true` | `false` makes the filter inert; a client sending `Idempotency-Key` is then simply not protected. |
| `Retention` | timespan | `24:00:00` | Sets each row's `ExpiresAt` — how long a *completed* response stays replayable. Must be > 0 and ≤ 30 days. See the note below on what actually enforces it. |
| `ClaimLease` | timespan | `00:15:00` | How long an *unfinished* claim blocks a retry before it is treated as abandoned and made reclaimable. Must be > 0 and ≤ `Retention`. |
| `MaxKeyLength` | int | `128` | Must be 1…512. A longer key is `400` `idempotency.keyInvalid`. |
| `MaxStoredResponseBytes` | int | `8192` | Must be ≥ 1. A larger response is stored without its body, and a replay then answers `409` `idempotency.notReplayable` rather than a truncated body. |

Read by `Src/Presentation/AppTemplate.Api/Common/Idempotency/IdempotencyExtensions.cs`. The filter is
registered globally but is **inert unless the action carries `[Idempotent]`** — only
`POST /api/v1/todo-lists` and `POST /api/v1/todo-lists/{id}/items` do. Sending no
`Idempotency-Key` is always allowed: the capability is available, not compulsory.

What each setting does when it is wrong:

- **`Retention` is a stamp, not a timer.** It only decides the `ExpiresAt` written on each row.
  Expiry is enforced by `DELETE /api/v1/maintenance/idempotency-keys/expired`, which requires the
  `Administrator` policy and which **nothing calls for you** — schedule it. Until a purge runs, a
  completed key stays replayable past its retention, and the table grows. That endpoint is also the
  template's worked example of policy-based authorisation.
- **`Retention` too short** (with purging in place) — a client retrying after a network stall past
  the window creates a second resource, which is the exact failure the feature exists to prevent.
- **`ClaimLease` is unrelated to `Retention`.** `Retention` governs how long a *completed* response
  stays replayable; `ClaimLease` only ever matters while a claim is still unfinished — it is what lets
  a retry take a key back from a claimant whose process died (a killed pod, an OOM) between claiming
  it and calling `CompleteAsync` or `ReleaseAsync`. Without it that key would answer `409`
  `idempotency.inProgress` for the rest of its 24-hour retention instead of minutes.
- **`ClaimLease` too short** — a lease shorter than the platform's own `RequestTimeouts:Extended`
  ceiling (10 minutes by default) can expire while a slow-but-legitimate write is still in flight,
  handing the same key to a second, concurrent retry: the exact double write this mechanism exists to
  prevent. The shipped default (15 minutes) is that ceiling plus headroom for clock drift and the
  little work left after the action returns; shrink `RequestTimeouts:Extended` before shrinking this.
- **`Retention` ≤ 0 or > 30 days**, **`ClaimLease` ≤ 0 or greater than `Retention`**,
  **`MaxKeyLength` outside 1…512**, or **`MaxStoredResponseBytes` < 1** — the host fails
  `ValidateOnStart()` and does not boot.

### `RequestLimits`

| Key | Type | Default | Notes |
|---|---|---|---|
| `MaxRequestBodyBytes` | long | `65536` | Must be between 1024 and 31457280 (30 MB). |

Read by `Src/Presentation/AppTemplate.Api/Common/Hosting/RequestLimitsExtensions.cs`. It replaces
Kestrel's 30 MB default, which is a free denial-of-service against an API whose largest legitimate
body is a few kilobytes.

- Enforced **twice, on purpose**: middleware rejects a request whose `Content-Length` exceeds the
  limit with `413` and `code: "request.tooLarge"`, and Kestrel's own `MaxRequestBodySize` is set
  from the same value as the backstop for a chunked request that sends no `Content-Length`. The
  middleware exists because integration tests run on `TestServer`, where Kestrel limits do not
  apply — a Kestrel-only limit would be untestable and therefore unverified.
- **Set too low** and legitimate requests start failing with `413`; the validator's 1024-byte floor
  stops the value going so low that nothing works at all.
- **Set outside the allowed range** and the host fails `ValidateOnStart()` and does not boot.

### `Shutdown`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Timeout` | timespan | `00:00:30` | Must be greater than zero and at most 10 minutes. |

Read by `Src/Presentation/AppTemplate.Api/Common/Hosting/HostLifecycleExtensions.cs`, which applies it to
the framework's own `HostOptions.ShutdownTimeout` — how long the host waits for in-flight requests
to drain once it starts stopping. 30 seconds matches the grace period Kubernetes gives a pod
(`terminationGracePeriodSeconds`) before sending SIGKILL, so this host is not still draining when
the orchestrator stops waiting for it.

**Readiness turns unhealthy the instant shutdown starts, before this timer even matters.**
`ShutdownHealthCheck` (tagged `ready`, alongside the database check) watches
`IHostApplicationLifetime.ApplicationStopping` and fails `/health/ready` the moment it fires, so an
orchestrator stops routing new traffic while this timeout is still draining what already arrived.
`/health` — liveness — does not carry this check and must never: failing liveness during a clean
shutdown would ask the orchestrator to kill a process that is exiting on its own.

**Set too short**, a slow-but-legitimate request (see `RequestTimeouts:Default` below on how long
one can legitimately run) is cut off mid-flight when the process actually stops, the same way a
crash would. **Set outside its range**, the host fails `ValidateOnStart()` and does not boot.

### `RequestTimeouts`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Default` | timespan | `00:05:00` | Applied to every endpoint that names no other policy. Must be 1 second – 1 hour. |
| `Extended` | timespan | `00:10:00` | Reachable only through the `long` named policy. Must be 1 second – 1 hour, and greater than `Default`. |

Read by `Src/Presentation/AppTemplate.Api/Common/Hosting/HostLifecycleExtensions.cs`, which installs
`AddRequestTimeouts`/`UseRequestTimeouts` with these two policies. A response still not started when
the deadline hits gets a `504` `ProblemDetails` with `code: "request.timeout"`; a response already
under way (headers or a first body chunk already sent) cannot be rewritten, so the connection is cut
instead — `GlobalExceptionHandler` is what classifies that case correctly in the logs, as a server
timeout rather than a client hangup.

**`Default` must stay longer than the persistence layer's own worst case, not shorter.**
`Database:CommandTimeoutSeconds` (30 s by default) and `EnableRetryOnFailure(maxRetryCount: 5)`
together mean one database call can legitimately occupy on the order of 230 seconds (up to six
attempts of 30 seconds, spaced by up to five 10-second backoffs) before it gives up on its own. A
shorter request timeout would routinely cancel a write that is still safely retrying underneath —
turning a transient failure the driver would have recovered from into a write whose outcome the
caller can no longer observe. If `Database:CommandTimeoutSeconds` or its retry budget ever change,
this default has to move with them; it must never be the one that moves first.

**`Extended`, named `long`, is for a future endpoint whose normal work — not a stream — legitimately
runs longer than `Default`** (a bulk import, say): still an ordinary request/response, so a timeout
can still answer a `ProblemDetails`. **A streaming endpoint (SSE, an `IAsyncEnumerable` response)
must use `[DisableRequestTimeout]` instead of either policy, never a larger number.** Once the first
byte is flushed there is no channel left to report a timeout on, so a "very large" value is not
safer, only later — it is still reached eventually, at the worst possible moment, and produces a
silent cutoff instead of a clean error either way.

**Set `Default` too low** and any endpoint whose work legitimately nears the numbers above starts
failing under normal load, not just under a real incident. **Set `Extended` below or equal to
`Default`**, or either outside 1 second – 1 hour, and the host fails `ValidateOnStart()` and does
not boot.

### `MaintenanceWorker` — `AppTemplate.Worker` only

| Key | Type | Default | Notes |
|---|---|---|---|
| `Interval` | timespan | `01:00:00` | How often the worker wakes up. Governs both tasks below. Range **1 second – 1 day**. |
| `PurgeExpiredIdempotencyKeysEnabled` | bool | `true` | Runs `IPurgeExpiredIdempotencyKeysUseCase` each iteration when `true`. |
| `PurgeExpiredRefreshTokensEnabled` | bool | `true` | Runs `IPurgeExpiredRefreshTokensUseCase` each iteration when `true`. |

Read by `AppTemplate.Worker/Features/Maintenance/MaintenanceWorkerOptions.cs`. Each task can be
switched off independently — an operator running the idempotency purge here but the refresh-token
purge some other way is not forced into both.

- Every iteration resolves both use cases from a fresh DI scope (`IServiceScopeFactory.CreateAsyncScope()`),
  never from a scope held for the process lifetime, so neither use case's scoped dependencies (a
  `DbContext`, a context factory) become an accidental singleton.
- A task that throws is logged and the loop retries at the next interval; it never stops the host,
  and it never stops its sibling task in the same iteration. A permanently broken idempotency purge
  must not also silence refresh-token cleanup.
- The loop honours cancellation immediately rather than waiting out the current `Interval`, and
  does not treat a shutdown mid-iteration as a failure to log.
- **Set outside 1 second – 1 day** and the host fails `ValidateOnStart()` and does not boot.

### Not configurable — and why that is worth knowing

| Behaviour | Value | Where |
|---|---|---|
| Auth rate limit | 10 requests/minute per client address | `Src/Presentation/AppTemplate.Api/Common/Security/RateLimitingExtensions.cs` |
| Global rate limit | 300 requests/minute per client address | same |
| Refresh token size / hash | 32 bytes CSPRNG / SHA-256 | `RefreshTokenGrants` |
| Aggregate item cap | 500 items per list | `TodoList.MaxItems` |
| Tag cap | 20 tags per item | `TodoItem.MaxTags` |
| Readiness checks | a DbContext check and a shutdown-state check, both tagged `ready` | `Program.cs`, `Common/Lifecycle/ShutdownHealthCheck.cs` |
| Max page size | 100 | `TodoListCollectionPolicy.MaxPageSize` |
| Default page size | 20 | `TodoListCollectionPolicy.DefaultPageSize` |
| Max sort terms | 3 | `TodoListCollectionPolicy.MaxSortTerms` |
| Sortable fields | `name`, `createdAt`, `lastModifiedAt` | `TodoListCollectionPolicy.SortableFields` |
| Max `search` length | 100 characters | `SearchTerm.MaxLength` |
| Max cursor length | 512 characters | `Cursor.MaxEncodedLength` |
| `Cache-Control` on reads | `private, no-cache` | `Src/Presentation/AppTemplate.Api/Common/Caching/CacheHeaderExtensions.cs` |

`Cache-Control` has no setting because there is only one defensible value for a per-user
authenticated response: caching here is revalidation, not storage. An endpoint whose response is identical for every
caller may set its own header; the middleware never overwrites one already present.

**Both rate limits are per instance, so the limit a caller actually meets is multiplied by your
replica count.** The limiter keeps its counters in the memory of one process; nothing is shared
between replicas. With the shipped defaults behind a load balancer spreading traffic evenly:

```
effective limit for one client address = 10 (or 300) x number of API replicas
```

Four replicas therefore admit 40 authentication attempts per minute per address, not 10. Size the
numbers against the replica count you actually run, and re-check them when you scale. This is a
deliberate trade rather than a gap: a shared counter means a shared store on the path of every
request, which buys exactness at the price of a new dependency the limiter must then survive the
loss of. Where a bound must hold across replicas — a tenant that may not spend another's budget —
the answer is a second deployment of the same image with its own ingress rules, not a distributed
counter.

Both limits also partition on the **client address**, including for authenticated callers, so
callers sharing an address share a budget. `Src/Presentation/AppTemplate.Api/Common/Security/RateLimiterPartitionKeys.cs`
explains why partitioning the global limiter by user identity is not available at the point the key
is computed, and why moving authentication earlier to make it available would cost more than it
saves. Behind a proxy this all depends on `ReverseProxy` being configured — see above; without it
every request carries the proxy's address and the whole deployment shares one partition.

The collection bounds are per-feature by design: they live on that feature's
`ICollectionPolicy`, not in configuration, because a deployment cannot know which of a
feature's columns are indexed. Raising `MaxPageSize` without measuring the query behind it
moves the cost onto the database, and widening `SortableFields` without adding the matching
index turns a page read into a sort of the whole table. See `docs/ADDING-A-FEATURE.md`.

These are compiled constants, not settings. If you need them per-environment, promote
them to an options class with a validator — do not read them straight from
`IConfiguration`.

### Standard ASP.NET Core keys

| Key | Notes |
|---|---|
| `Logging:LogLevel:*` | Standard. The provider is `AddJsonConsole`, so output is structured JSON, not the default console format. |
| `AllowedHosts` | Host filtering. `*` in the tracked defaults. |
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Staging`, `Production`. Gates `appsettings.Development.json`, OpenAPI/Scalar, startup migrations and identity seeding. |
| `ASPNETCORE_HTTP_PORTS` | `8080` in the container. Do **not** also set `ASPNETCORE_URLS`; it takes precedence and would silently ignore this. |

## Environment-variable mapping

`docker-compose.yml` maps every section from `.env`. The pattern:

| Configuration key | Environment variable |
|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` |
| `Jwt:Key` | `Jwt__Key` |
| `Identity:PasswordRequiredLength` | `Identity__PasswordRequiredLength` |
| `Email:AllowInsecureTransport` | `Email__AllowInsecureTransport` |
| `Cors:AllowedOrigins[0]` | `Cors__AllowedOrigins__0` |
| `Database:MaxPoolSize` | `Database__MaxPoolSize` |
| `IdempotencyPurge:BatchSize` | `IdempotencyPurge__BatchSize` |
| `MaintenanceWorker:Interval` | `MaintenanceWorker__Interval` |

Colons do not work as separators in environment variables on all platforms; the double
underscore always does.

`docker-compose.yml` deliberately has **no `env_file:`** on any service. Each variable
is mapped explicitly under `environment:`, so a container holds only the keys it needs;
`env_file: .env` would additionally push `POSTGRES_PASSWORD`, `JWT_KEY` and every other
raw variable into the API process, readable from `/proc/self/environ` and from any
crash dump, for no benefit.

`${VAR:?message}` in the compose file makes Compose fail loudly with that message
rather than substituting an empty value. `docker compose config` is the cheap check
that `.env.example` still satisfies every one of those guards; CI runs it.
