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
> | `Jwt`, `Identity`, `RefreshToken`, `EmailConfirmation` | `AddIdentityModule` | `AppTemplate.Infrastructure.Identity/Options/` |
> | `IdentitySeed` | `AddPersistenceModule` | `AppTemplate.Infrastructure.Persistence/Features/Identity/Seeding/` |
> | `Email` | `AddEmailModule` | `AppTemplate.Infrastructure.Email/Options/` |
>
> `IdentitySeed` sits with the seeder because seeding is a persistence concern, not an
> authentication policy. **The configuration keys and their validation do not change** —
> only the file paths and which DI extension method binds them. Treat any path in this
> document as indicative and the key names as authoritative.

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

Reconciled against `Src/Presentation/AppTemplate.Api/Common/Security/CorsPolicies.cs`, which reads the key
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

Read by `Src/Presentation/AppTemplate.Api/Common/Security/ForwardedHeadersPolicies.cs`. This is the
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

Read by `Src/Presentation/AppTemplate.Api/Common/Security/SecurityHeadersPolicies.cs`. Alongside the CSP,
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

**The application sends no `Strict-Transport-Security` header** — see `docs/adr/0012`. An ingress
terminating TLS is required to send it.

### `OpenTelemetry`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Off registers no instrumentation, no exporter and no background flush. |
| `OtlpEndpoint` | string | `""` | e.g. `http://collector:4317`. |
| `OtlpProtocol` | string | `Grpc` | `Grpc` or `HttpProtobuf`. |
| `ServiceName` | string? | `null` | Falls back to the assembly name and informational version. |

Read by `Src/Presentation/AppTemplate.Api/Common/Observability/ObservabilityPolicies.cs`. Traces cover
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

Read by `Src/Presentation/AppTemplate.Api/Common/Concurrency/ConcurrencyPolicies.cs`. Every read of a
`TodoList` or `TodoItem` publishes a strong `ETag` regardless of this setting, and every write
already honours `If-Match` when one is sent — a stale, malformed or unrecognised version is
refused with `412` either way. This setting only decides what happens when a write names **no**
version at all.

- **Left at `Optional`, the template behaves exactly as it did before conditional requests
  existed**: an unconditional write still succeeds, guarded only by the `xmin` check inside the
  use case. This is deliberate — see `docs/adr/0013` — so that adding `If-Match` support does not
  silently start rejecting every client that predates it.
- **Set to `Required` only once every client of this deployment reads before it writes** and
  echoes back the `ETag` it read; otherwise every mutation starts answering `428`.
- Bound once at startup and validated: a value that is not one of the two enum members fails
  `ValidateOnStart()` rather than falling through to the more permissive branch.

### Not configurable — and why that is worth knowing

| Behaviour | Value | Where |
|---|---|---|
| Auth rate limit | 10 requests/minute per client address | `Src/Presentation/AppTemplate.Api/Common/Security/RateLimitingPolicies.cs` |
| Global rate limit | 300 requests/minute per client address | same |
| Refresh token size / hash | 32 bytes CSPRNG / SHA-256 | `RefreshTokenGrants` |
| Aggregate item cap | 500 items per list | `TodoList.MaxItems` |
| Tag cap | 20 tags per item | `TodoItem.MaxTags` |
| Readiness check | one DbContext check, tagged `ready` | `Program.cs` |

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
