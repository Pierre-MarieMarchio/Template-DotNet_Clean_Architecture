# Configuration

Every setting the application reads is listed here — this page, not a file, is the complete
list. The tracked `Src/Presentation/AppTemplate.Api/appsettings.json` is the schema of record for
everything the API host must be told, and contains **no secrets**: every secret-shaped value in it
is an empty string.
<!--#if (localAuth)-->
Five sections whose defaults are all safe are omitted from it and documented
here instead — `Database`, `IdempotencyPurge`, `IdentityTokens`, `ExternalIdentity` and `Postmark`
<!--#else
Three sections whose defaults are all safe are omitted from it and documented
here instead — `Database`, `IdempotencyPurge` and `Postmark`
<!--#endif-->
— as is `Idempotency:ClaimLease`, whose four siblings are there. An absent section is not an absent
key: each of them binds and validates exactly as the ones written out do.

## How configuration is loaded

Sources, later overriding earlier:

1. `appsettings.json` — committed, non-secret, every key the host must be told (see above for
   the sections it deliberately leaves to their defaults).
2. `appsettings.Development.json` — committed, localhost throwaways. Loaded **only**
   when `ASPNETCORE_ENVIRONMENT=Development`, and **only** under that exact spelling.
3. **User secrets** — local machine, never committed. `UserSecretsId` is already set
   in `AppTemplate.Api.csproj`.
4. **Environment variables** — how deployed environments and `docker compose` supply
   values. Nest sections with a double underscore: `ConnectionStrings:Default` becomes
   `ConnectionStrings__Default`.

## Validation happens at startup, not on first use

Every section below binds to an options class with an `IValidateOptions<T>` validator
registered with `.ValidateOnStart()`. A missing or
out-of-range value **fails the host at startup** with a message naming the exact key.

<!--#if (localAuth)-->
Verified — blanking `EmailConfirmation:ConfirmEmailUrl` and shortening `Jwt:Key`:

```
Hosting failed to start
System.AggregateException: One or more errors occurred.
  ('Jwt:Key' must be at least 32 bytes long to sign HS256 tokens.)
  ('EmailConfirmation:ConfirmEmailUrl' is required.)
```

<!--#endif-->
Every failing key is reported in one pass, before Kestrel binds a port. An empty
string in `appsettings.json` therefore behaves as "you must supply this", not as a
value that silently works.

This means `appsettings.json` alone will not boot the app — by design. The blanks must
be filled from user secrets or environment variables.

> **Where the options classes live.** Each section is bound and validated by the module that
> consumes it, and inside that module a transverse section sits under `Common/Options/` while
> one that belongs to a single feature sits under `Features/<Feature>/Configuration/Options/` — the same
> partition every infrastructure module uses for everything else.
>
> | Section | Bound by | Declared in |
> |---|---|---|
<!--#if (localAuth)-->
> | `Identity`, `IdentityTokens` | `AddAuthModule` | `AppTemplate.Infrastructure.Auth/Common/Options/` |
> | `Jwt`, `RefreshToken`, `SecurityEventLog`, `EmailConfirmation`, `PasswordReset`, `EmailChange`, `TwoFactor` | `AddAuthModule` | `AppTemplate.Infrastructure.Auth/Features/<Feature>/Configuration/Options/` |
> | `IdentitySeed` | `AddAuthModule` | `AppTemplate.Infrastructure.Auth/Features/Accounts/Configuration/Seeding/` |
<!--#endif-->
<!--#if (externalAuth)-->
> | `Authentication:External` | `AddExternalAuthentication` | `AppTemplate.Api/Features/ExternalIdentity/Configuration/Options/` |
<!--#endif-->
<!--#if (localAuth)-->
> | `Database` | `AddPersistenceModule` **and** `AddAuthModule` | `AppTemplate.Infrastructure.Core/Common/Options/` |
<!--#else
> | `Database` | `AddPersistenceModule` | `AppTemplate.Infrastructure.Core/Common/Options/` |
<!--#endif-->
> | `IdempotencyPurge` | `AddPersistenceModule` | `AppTemplate.Infrastructure.Persistence/Common/Idempotency/` |
> | `Email`, `Postmark` | `AddEmailModule` | `AppTemplate.Infrastructure.Email/Common/Smtp/` and `Common/Http/` |
<!--#if (files)-->
> | `Files` (its categories and `ApiUploadMaxBytes`) | `AddFiles`, the application's Files entry point; `AddStorageModule` adds the checks against scanning and the provider | `AppTemplate.Application/Features/Files/Configuration/Options/` |
> | `Files:Storage:S3` | `AddStorageModule`, when `Files:Storage:Provider` is `S3` | `AppTemplate.Infrastructure.Storage/Common/Options/` |
> | `Files:Storage`, `Files:Storage:FileSystem`, `Files:Scanning` | `AddStorageModule` (the `FileSystem` section only for that provider) | `AppTemplate.Infrastructure.Storage/Features/Files/Configuration/Options/` |
<!--#endif-->
> | `Jobs:Enabled`, and each feature's own `Jobs:…` section | `AddBackgroundJobs`, and only when `Jobs:Enabled` is true | `AppTemplate.Api/Common/Jobs/` and `AppTemplate.Api/Features/<Feature>/Jobs/` |
> | `OpenTelemetry` | `AddObservability`, through the API's `AddApiObservability` | `AppTemplate.Presentation.Core/Common/Observability/` |
> | `Localization` | `AddLocalizationOptions`, through the API's `AddRequestLanguage` | `AppTemplate.Presentation.Core/Common/Localization/` |
> | Everything else | `AppTemplate.Api`'s own `Program.cs`, through `AddHttpPipeline` | beside the middleware that reads it, under `AppTemplate.Api/Common/` |
>
> **The configuration keys and their validation do not change** —
> only the file paths and which DI extension method binds them. Treat any path in this
> document as indicative and the key names as authoritative.
>
> One consequence of the `Options/` folder name is worth knowing before you add another: it
> puts an `Options` namespace under the module's own root, which shadows
> `Microsoft.Extensions.Options.Options` for every file beneath it — so the short
> `Options.Create(…)` stops resolving in the test project that mirrors it.
<!--#if (files && localAuth)-->
> The two projects
> that meet this say so where they work around it: `StorageFixture` writes the fully
> qualified `Microsoft.Extensions.Options.Options.Create`,
> and `AccountEmailFactoryConfirmationTests` uses `OptionsWrapper<T>` instead.
<!--#elseif (files)-->
> The project
> that meets this says so where it works around it: `StorageFixture` writes the fully
> qualified `Microsoft.Extensions.Options.Options.Create`.
<!--#elseif (localAuth)-->
> The project
> that meets this says so where it works around it:
> `AccountEmailFactoryConfirmationTests` uses `OptionsWrapper<T>` instead.
<!--#endif-->

## One process: HTTP and the periodic jobs

<!--#if (reminders && files)-->
`AppTemplate.Api` serves HTTP and, by default, runs the periodic jobs in the same process: the
maintenance job, the reminder pass and the three file passes.
<!--#elseif (reminders)-->
`AppTemplate.Api` serves HTTP and, by default, runs the periodic jobs in the same process: the
maintenance job and the reminder pass.
<!--#elseif (files)-->
`AppTemplate.Api` serves HTTP and, by default, runs the periodic jobs in the same process: the
maintenance job and the three file passes.
<!--#else-->
`AppTemplate.Api` serves HTTP and, by default, runs the periodic jobs in the same process: the
maintenance job.
<!--#endif-->
There is no second host and no
second configuration schema — every section below is read by the one process, and `AddBackgroundJobs`
(`Src/Presentation/AppTemplate.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs`) is the
single call that composes the jobs.
<!--#if (accounts)-->
The jobs call the same application-layer use cases the controllers
do — `MaintenanceController` exposes the idempotency purge over HTTP, for example — on a timer instead of a
request.
<!--#else-->
The jobs call application-layer use cases, as a controller does, on a timer instead of a request.
<!--#endif-->
<!--#if (reminders)-->
A due reminder is rung through the same `IReminderNotifier` port.
<!--#endif-->

**`Jobs:Enabled` is a deployment role, not a job setting.** It defaults to `true`, so a
single instance does everything. Set it to `false` and the instance still serves every route, but
starts no periodic pass — and binds none of the job sections, so it is not made to supply or
validate settings it never reads. When it is `true`, each job's section is bound and validated at
startup like any other: an invalid interval fails the host with a message naming the key, and is
never quietly read as "disabled". A job is disabled only by its own `Enabled` flag, which keeps
counting and logging as disabled on every tick.

**Several instances.** Set `Jobs__Enabled=false` on every instance that should only serve
HTTP and leave it `true` on exactly one designated instance.
<!--#if (files)-->
That keeps the file passes — which walk
the object store — from being multiplied by the number of replicas, without a queue or a global
scheduler.
<!--#endif-->
The protections underneath stay in place and are not a substitute for that choice:
<!--#if (reminders && files)-->
the reminder pass and the three file passes take the leader lease inside their use cases, and the
other passes are idempotent.
<!--#elseif (reminders)-->
the reminder pass takes the leader lease inside its use case, and the other passes are idempotent.
<!--#elseif (files)-->
the three file passes take the leader lease inside their use cases, and the other passes are
idempotent.
<!--#else-->
every pass is idempotent.
<!--#endif-->
<!--#if (accounts)-->
A rolling deployment, a manual `MaintenanceController` call or a mistaken double-enable can overlap
without corrupting anything.
<!--#else-->
A rolling deployment or a mistaken double-enable can overlap without corrupting anything.
<!--#endif-->
They can still repeat work.
<!--#if (reminders)-->
The lease is not a fencing token, so reminder delivery remains at-least-once.
<!--#endif-->
**`/health/ready` reports HTTP readiness** — the database
checks and the shutdown state — and says nothing about whether the jobs run: watch the designated
instance's job heartbeats (the `iterations` counters under [`OpenTelemetry`](#opentelemetry)) for that.

**A separate runner, later.** If measured load or release needs justify a process of its own, the
jobs' hosting adapters and their one composition call move into it, and the API sets
`Jobs:Enabled=false`. That process supplies its own caller and audit identity
(`AddNoCallerIdentity()` and an `IAuditActor` that answers nobody, both of which already exist) and
its own telemetry. Use cases, contracts and storage guarantees do not move.

<!--#if (reminders && localAuth)-->
**What the jobs need from the other modules.** The reminder pass reaches its owner through
`IReminderRecipientDirectory`, which Reminders owns and the host supplies — over the local accounts in
this template — and mails them through `IReminderNotifier`, whose only adapter lives in
`AppTemplate.Infrastructure.Email`, which cannot be composed without validating `Email` at startup.
No job needs an authentication port of its own: `TheBackgroundOnlyContainer_BuildsWithoutAuthentication`
holds that. A process that composes the identity module — the API does, for the account routes —
validates the `Jwt`, `RefreshToken`, `EmailConfirmation`, `PasswordReset` and `EmailChange` sections
as a whole, since composing that module composes ASP.NET Identity and bearer validation with no
narrower call.
<!--#elseif (reminders)
**What the jobs need from the other modules.** The reminder pass reaches its owner through
`IReminderRecipientDirectory`, which Reminders owns and the host supplies — here from the address the
identity provider last sent, kept on the caller's mapping, since a pass has no token to read it from —
and mails them through `IReminderNotifier`, whose only adapter lives in
`AppTemplate.Infrastructure.Email`, which cannot be composed without validating `Email` at startup.
The API validates `Authentication:External` at startup, because it composes bearer validation with
the mapping.
<!--#elseif (localAuth)
**What the jobs need from the other modules.** No job needs an authentication port of its own:
`TheBackgroundOnlyContainer_BuildsWithoutAuthentication` holds that. A process that composes the
identity module — the API does, for the account routes — validates the `Jwt`, `RefreshToken`,
`EmailConfirmation`, `PasswordReset` and `EmailChange` sections as a whole, since composing that
module composes ASP.NET Identity and bearer validation with no narrower call.
#else
**What the jobs need from the other modules.** No job needs an authentication port of its own. The
API validates the sections of whatever module it composes at startup, as a whole.
#endif-->

<!--#if (localAuth)-->
**Authentication's two purges belong to authentication.** They are not listed in the background
composition: `AddAuthFeature` (`Src/Presentation/AppTemplate.Api/Features/Auth/Composition/AuthComposition.cs`)
registers them with the maintenance timer, from the authentication feature's own folder, and
[`Jobs:Maintenance:PurgeExpiredRefreshTokensEnabled`](#jobsmaintenancepurgeexpiredrefreshtokensenabled)
and
[`Jobs:Maintenance:PurgeExpiredSecurityEventsEnabled`](#jobsmaintenancepurgeexpiredsecurityeventsenabled)
are read by an options class that lives beside them. A project built without authentication deletes that
call and that folder and edits nothing in the composition — `BackgroundWorkTests` fails the build if
the composition ever names authentication.

<!--#endif-->
**`ICurrentUser` and the audit actor in a job.** One container serves both, so the caller and the
audit actor are registered once and answer by the kind of scope they are resolved in. A request keeps
its principal and its audit attribution, an anonymous request included. A job opens every pass
through `CreateBackgroundScope`, and in that scope `ICurrentUser` is `NoCallerCurrentUser`, whose
`UserId` throws `NotSupportedException` — a use case that reads the caller there must fail loudly,
not proceed as though the request were anonymous — and `IAuditActor` is `BackgroundAuditActor`,
which records a null user because nobody made the write. Nothing configures this.

## Secrets: use `dotnet user-secrets`

`Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj` carries a `UserSecretsId`, so the store already exists.
Values live outside the repository (on Windows,
`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`) and override both appsettings
files.

```bash
cd Src/Presentation/AppTemplate.Api

# The one thing you actually need to run locally
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=<your password>;Gss Encryption Mode=Disable"
<!--#if (localAuth)-->
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
<!--#endif-->

dotnet user-secrets list      # show what is set
dotnet user-secrets remove "ConnectionStrings:Default"
dotnet user-secrets clear
```

Never put a secret in `appsettings.json` or `appsettings.Development.json` — both are
tracked in git.

## Reference

Types are the bound CLR types. "Required" means startup fails without it. Defaults are
the property initialisers on the options class, which apply when the key is absent —
they are **not** the values in `appsettings.Development.json`, which override them.

**Find your section.** The two entries at the end are the ones to read even when no key sends you
there: what is deliberately not configurable, and how a key becomes an environment variable.

| | Sections |
|---|---|
<!--#if (files)-->
| Database and storage | [`ConnectionStrings`](#connectionstrings) · [`Database`](#database) · [`Files`](#files) · [`Files:Storage`](#filesstorage) · [`Files:Storage:S3`](#filesstorages3) · [`Files:Storage:FileSystem`](#filesstoragefilesystem) · [`Files:Scanning`](#filesscanning) |
<!--#else
| Database and storage | [`ConnectionStrings`](#connectionstrings) · [`Database`](#database) |
<!--#endif-->
<!--#if (localAuth)-->
| Identity and tokens | [`Jwt`](#jwt) · [`RefreshToken`](#refreshtoken) · [`SecurityEventLog`](#securityeventlog) · [`Identity`](#identity) · [`TwoFactor`](#twofactor) · [`Passkey`](#passkey) · [`IdentityTokens`](#identitytokens) · [`ExternalIdentity`](#externalidentity) · [`IdentitySeed`](#identityseed--development-only) |
<!--#endif-->
<!--#if (externalAuth)-->
| External authentication | [`Authentication:External`](#authenticationexternal) |
<!--#endif-->
<!--#if (localAuth)-->
| Mail | [`Email:Transport`](#emailtransport--smtp-or-an-http-api) · [`Email`](#email) · [`Postmark`](#postmark) · [`EmailConfirmation`](#emailconfirmation) · [`PasswordReset`](#passwordreset) · [`EmailChange`](#emailchange) · [`Localization`](#localization) |
<!--#else
| Mail | [`Email:Transport`](#emailtransport--smtp-or-an-http-api) · [`Email`](#email) · [`Postmark`](#postmark) · [`Localization`](#localization) |
<!--#endif-->
| The HTTP boundary | [`Cors`](#cors) · [`ReverseProxy`](#reverseproxy) · [`SecurityHeaders`](#securityheaders) · [`RequestLimits`](#requestlimits) · [`RequestTimeouts`](#requesttimeouts) · [`ProblemTypes`](#problemtypes) · [`RateLimiting`](#ratelimiting) |
| Correctness mechanisms | [`Concurrency`](#concurrency) · [`Idempotency`](#idempotency) · [`IdempotencyPurge`](#idempotencypurge) |
| Lifecycle and observability | [`Shutdown`](#shutdown) · [`OpenTelemetry`](#opentelemetry) · [standard ASP.NET Core keys](#standard-aspnet-core-keys) |
<!--#if (files && reminders)-->
| Background jobs | [`Jobs`](#jobs) · [`Jobs:Files`](#jobsfiles) · [`Jobs:Maintenance`](#jobsmaintenance) · [`Jobs:Reminders`](#jobsreminders) |
<!--#elseif (files)-->
| Background jobs | [`Jobs`](#jobs) · [`Jobs:Files`](#jobsfiles) · [`Jobs:Maintenance`](#jobsmaintenance) |
<!--#elseif (reminders)-->
| Background jobs | [`Jobs`](#jobs) · [`Jobs:Maintenance`](#jobsmaintenance) · [`Jobs:Reminders`](#jobsreminders) |
<!--#else
| Background jobs | [`Jobs`](#jobs) · [`Jobs:Maintenance`](#jobsmaintenance) |
<!--#endif-->
| Read these anyway | [not configurable, and why](#not-configurable--and-why-that-is-worth-knowing) · [outbound HTTP](#outbound-http--deliberately-not-configurable) · [environment-variable mapping](#environment-variable-mapping) |

### `ConnectionStrings`

| Key | Type | Default | Notes |
|---|---|---|---|
<!--#if (localAuth)-->
| `Default` | string | — | **Required.** The one and only connection string. Npgsql format. Both `DbContext`s use it, which is also what makes them share one pool; see [ARCHITECTURE.md](ARCHITECTURE.md#two-contexts-one-database). |
<!--#else
| `Default` | string | — | **Required.** The one and only connection string. Npgsql format. |
<!--#endif-->

Example: `Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=…;Gss Encryption Mode=Disable`

The shipped connection strings set `Gss Encryption Mode=Disable` because Npgsql otherwise probes for Kerberos (GSSAPI) encryption first, which logs a missing `libgssapi_krb5` error on Alpine images before falling back. It leaves TLS (`SSL Mode`) and password authentication untouched; a Kerberos deployment removes it.

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
20 is deliberately conservative so that running several replicas of the API against one
PostgreSQL instance does not by itself approach the server's ceiling.

<!--#if (localAuth)-->
**Bound once, for both contexts.** This section is bound by whichever modules own a context —
`AddPersistenceModule` and `AddAuthModule` — into one options type in
`AppTemplate.Infrastructure.Core`. Npgsql pools per connection string, so two contexts built on the
identical string share **one** pool of `MaxPoolSize`, not one each. Deriving the bound separately in
the two modules is what would turn it into two.

<!--#endif-->
**Sizing this against replica count and `max_connections`.** Every replica of every process that
composes either module — each API instance, whether or not it runs the jobs — holds its own pool up
to `MaxPoolSize`. Budget:

```
sum over every replica of (that replica's Database:MaxPoolSize)
  + PostgreSQL's own reserved connections (superuser_reserved_connections, default 3)
  <= max_connections
```

With the shipped default of 20 and `max_connections=100`, that is room for **4–5 replicas total**
before the server itself starts refusing connections — and refusing a
connection is not a graceful degradation, it is a hard failure of every feature that touches the
database at once. Raising `max_connections` costs PostgreSQL memory per slot; lowering
`MaxPoolSize` costs this process concurrency headroom under its own load. Pick the one that is
cheaper for your deployment, but pick one — the failure mode of picking neither is silent until
the day a second replica starts.

**The idempotency store's extra connections count against this same budget, not a separate one.**
`IIdempotencyStore` is backed by `IDbContextFactory<AppDbContext>` rather than the request's
ambient `AppDbContext` — [`DECISIONS.md`](DECISIONS.md#infrastructure) says why that has to be a
second, independently-committed connection. In practice that means **one idempotent write can hold up to
three connections from this same pool at once**: the ambient request connection, the factory
connection the claim is written through, and the factory connection the claim is completed
through. A capacity plan that only counts "one connection per request" under-provisions for every
idempotent endpoint. `AddContextFactory` deliberately shares `AddContext`'s options object rather
than building a second one, specifically so both draw from the one pool this setting bounds
instead of a second, unbounded one.

**The jobs draw from the same pool as requests.** An instance with `Jobs:Enabled=true`
runs its passes on connections from its own
`Database:MaxPoolSize`, beside the requests it is serving. Leave headroom for a pass running during
load rather than sizing the pool for requests alone.

<!--#if (reminders || files)-->
**The leader lease sits outside the pool, and that is deliberate.** `PostgresLeaderLease` opens its
own `NpgsqlConnection` with `Pooling=false` for the duration of one guarded run, so a host running
a leased pass holds `MaxPoolSize + 1` connections rather than borrowing a pooled one. A pooled
connection would be the wrong instrument twice over: the pool exists to amortise connections held
for milliseconds, where a lease is held for as long as the work takes, and the lock's guarantee
*is* the session's lifetime — it must disappear when the process does, not when a pooled connection
is next recycled. Add one connection per replica that runs a leased loop to the budget above.

**A transaction-pooling proxy in front of PostgreSQL breaks the lease, silently.** `pg_try_advisory_lock`
is session-scoped, and pgBouncer in `transaction` or `statement` mode hands a client a different
backend per transaction — so the lock would be taken on one backend and looked for on another,
every host would believe it is the leader, and nothing would fail. Nothing in this repository interposes
a pooler today. If you add one, give this connection a `session`-mode route or a direct one.

<!--#endif-->
### `Email:Transport` — SMTP or an HTTP API

`Email:Transport` picks which adapter satisfies `IEmailSender`. It defaults to `Smtp`, which is what
every deployment cloned from this template before the option existed was already doing — a different
default would have been a silent behaviour change for a repository that is cloned rather than
referenced. An unrecognised value stops the process at start-up rather than falling back.

| Value | Adapter | Section it needs |
|---|---|---|
| `Smtp` (default) | MailKit over SMTP | `Email:Host`, `Email:Port`, `Email:Security`, … |
| `Postmark` | a typed `HttpClient` against Postmark's API | `Postmark:*` |

**Why a third adapter exists at all.** `IEmailSender` had two, and one of them was a test double —
which proves very little about a port. A third whose transport is radically different is what shows
the seam is real. It is also what a deployment needs: **outbound SMTP is blocked at most hosting
providers**, so a template that can only send by SMTP cannot send in production.

**The SMTP settings stop being required when the transport is HTTP**, which is the point — an
operator using Postmark has no SMTP server and should not have to invent a hostname for one.
`Email:FromAddress` and `Email:FromName` stay required either way: sender identity is not a property
of the transport.

**There is no retry on a failed send, deliberately.** The outbound policy retries safe verbs only,
and sending mail is a `POST`.
<!--#if (localAuth)-->
That matches a decision this template already made: `RegisterUseCase`
answers a delivery failure exactly as a delivered mail, because the account was created and the
caller can ask for the mail again.
<!--#endif-->

### `Postmark`

Bound only when `Email:Transport` is `Postmark`.

| Key | Type | Default | Secret |
|---|---|---|---|
| `ServerToken` | string | `""` | **yes** — empty in every tracked file |
| `ApiBaseUrl` | string | `https://api.postmarkapp.com/` | no — must be an absolute `http`/`https` URL. Plain `http` is refused against anything but loopback, because the server token travels on it. |
| `MessageStream` | string | `outbound` | no — **must not be blank.** Postmark applies no implicit stream, so an empty value is a refusal rather than a default. |

### `RateLimiting`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Window` | timespan | `00:01:00` | Length of the fixed window the limits count over. Must be 1 second – 1 day. |
<!--#if (localAuth)-->
| `AuthenticationPermitLimit` | int | `10` | Requests per window and client address on the authentication controller (policy `authentication`). Must be at least 1. |
<!--#endif-->
| `GlobalPermitLimit` | int | `300` | Requests per window and client address on every request. Must be at least 1. |

Read by `Src/Presentation/AppTemplate.Api/Common/Security/RateLimitingExtensions.cs`. A refused
request answers `429` with `Retry-After` and a `ProblemDetails` body with
`code: "rateLimit.exceeded"`.

**The rate limits are per instance, so the limit a caller actually meets is multiplied by your
replica count.** The limiter keeps its counters in the memory of one process; nothing is shared
between replicas.
<!--#if (localAuth)-->
With the shipped defaults behind a load balancer spreading traffic evenly:

```
effective limit for one client address = AuthenticationPermitLimit (or GlobalPermitLimit) x number of API replicas
```

Four replicas therefore admit 40 authentication attempts per minute per address, not 10.
<!--#endif-->
Size the numbers against the replica count you actually run, and re-check them when you scale. This is a
deliberate trade rather than a gap: a shared counter means a shared store on the path of every
request, which buys exactness at the price of a new dependency the limiter must then survive the
loss of. Where a bound must hold across replicas — a tenant that may not spend another's budget —
the answer is a second deployment of the same image with its own ingress rules, not a distributed
counter.

The limits also partition on the **client address**, including for authenticated callers, so
callers sharing an address share a budget. `Src/Presentation/AppTemplate.Api/Common/Security/RateLimiterPartitionKeys.cs`
explains why partitioning the global limiter by user identity is not available at the point the key
is computed, and why moving authentication earlier to make it available would cost more than it
saves. Behind a proxy this all depends on `ReverseProxy` being configured — see [`ReverseProxy`](#reverseproxy); without it
every request carries the proxy's address and the whole deployment shares one partition.

**Redis is deliberately not added.** This template already refused output caching and a
security-stamp cache with reasons written down, so it has no distributed cache to reuse — and
bringing a vendor in for a single capability is exactly the weight it avoids. A deployment that
needs a shared count replaces the registration in `RateLimitingExtensions` with a limiter of its
own, which must keep writing `Retry-After`.

**Set `Window` or a limit outside its range** and the host fails `ValidateOnStart()` and does not
boot.

<!--#if (localAuth)-->
### `ExternalIdentity`

Which providers a caller may present an `id_token` from. Bound by `AddAuthModule`, so both hosts
read it; only the API acts on it.

| Key | Type | Default | Notes |
|---|---|---|---|
| `KeySetLifetime` | TimeSpan | `00:15:00` | How long a provider's signing keys are cached. Range 1 minute–24 hours. |
| `Providers:<n>:Name` | string | — | What the client names in the route. An operator's word, not the template's. |
| `Providers:<n>:Issuers:<m>` | string | — | At least one. The `iss` the token must carry. |
| `Providers:<n>:Audiences:<m>` | string | — | At least one. Your client id at that provider. |
| `Providers:<n>:JwksUri` | string | — | Absolute https. **Exactly one** of this and `MetadataAddress`. |
| `Providers:<n>:MetadataAddress` | string | — | Absolute https. OIDC discovery, if you prefer it to a fixed JWKS URI. |

**There is no secret here, and there should not be.** Verifying an `id_token` is asymmetric
cryptography: the template needs the provider's public keys and nothing else. If you find yourself
wanting to configure a client secret, you are holding the wrong flow — this is not the authorisation
code exchange, which happens in the client.

**An empty list is a valid configuration.** A deployment that uses no external provider starts
normally and refuses every external sign-in. A feature nobody enables must not be able to stop the
process from booting.

**One adapter, not one per provider.** Google, Microsoft and Apple differ in *values*, not in
behaviour — they all speak OIDC — so adding Okta or Keycloak is a configuration entry rather than a
class. Apple's one real quirk (the address arrives only at the first authorisation) is handled where
it belongs, in the use case, because it is about linking rather than about verification.

<!--#endif-->
<!--#if (externalAuth)-->
### `Authentication:External`

The external OpenID Connect provider (Keycloak, Entra ID, Auth0…) whose access tokens authenticate
callers in place of the local accounts. Bound by `AddExternalAuthentication`, the API's entry point
for that mode; a host with local accounts never reads it.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Authority` | string | `""` | **Required.** The provider's issuer URL; its discovery document supplies the signing keys and the issuer a token must carry. |
| `Audience` | string | `""` | **Required.** This API's identifier at the provider. Audience validation is never disabled. |
| `RequireHttpsMetadata` | bool | `true` | Refuses an `http` authority. Only a provider on a developer's machine has business setting `false`. |
| `SubjectClaim` | string | `sub` | The claim naming the caller at the provider. Mapped to an internal owner id, never used as one. |
| `EmailClaim` | string | `email` | Kept on the mapping, for notifications — dropped when the token carries `email_verified` and it is not `true`, kept when the claim is absent. |
| `NameClaim` | string | `name` | Kept on the mapping, for notifications. |
| `Roles:Source` | `Claim` or `Local` | `Local` | Where roles come from: the token, or the `external_identity.RoleAssignments` table. |
| `Roles:ClaimPath` | string | — | **Required when `Source` is `Claim`.** A dot path to a string or an array of strings: `roles` (Entra ID), `realm_access.roles` (Keycloak), a namespaced claim such as `https://example.com/roles` (Auth0, matched whole). |
| `Roles:AdministratorRole` | string | `Admin` | The role the `Administrator` policy requires. |

**The subject is never the owner.** Each (issuer, subject) pair is mapped to an internal `UserId`
the first time it calls, and every business row carries that id; an email address is never the key.
[`SECURITY.md`](../SECURITY.md#external-identity-provider) has the mapping, how to keep existing
owners across a change of provider, and why a token that cannot be mapped is refused.

**An address is kept only if the provider does not call it unverified.** Reminders are sent to it,
so a token whose `email_verified` is anything but `true` keeps none. A token without the claim keeps
its address, because Entra ID and others never send it; a provider that issues unverified addresses
*and* omits the claim should have `EmailClaim` pointed at a claim it never emits.

**`Local` roles have no API.** Assignments are rows of `(UserId, Role)` written by SQL or a seed.

<!--#endif-->
<!--#if (files)-->
### `Jobs:Files`

The Files feature's two sweeps and its inspection pass. Bound by `AddBackgroundJobs`, and only when
`Jobs:Enabled` is true: an instance that runs no jobs never reads it.

| Key | Type | Default | Notes |
|---|---|---|---|
| `PurgeAbandonedRegistrationsInterval` | TimeSpan | `01:00:00` | |
| `PurgeAbandonedRegistrationsEnabled` | bool | `true` | Retires `Pending` rows whose upload never happened. |
| `ReclaimOrphanedContentInterval` | TimeSpan | `12:00:00` | Each pass walks up to 250,000 keys from where the last one stopped; that is why it is hours, not minutes. |
| `ReclaimOrphanedContentEnabled` | bool | `true` | **Do not leave this off.** |
| `InspectDepositedFilesInterval` | TimeSpan | `00:01:00` | Range 10 s–1 h. This is a **user-visible latency**: a file is not readable until this runs. |
| `InspectDepositedFilesEnabled` | bool | `true` | **Off stops the feature**, it does not degrade it: no upload ever becomes readable. |

**The reclamation sweep is not housekeeping, it is the feature's correctness guarantee.** There is no
soft delete: `DELETE` removes the row, and the bytes are only ever removed because this pass
re-derives "an object no row references is garbage". Turn it off and storage grows silently, with
nothing to say so. The deletion domain event reclaims promptly as a fast path, and — per this
repository's standing rule — nothing depends on it having run.

Each pass runs under the leader lease (`apptemplate.files.inspect-deposited`,
`apptemplate.files.purge-abandoned`, `apptemplate.files.reclaim-orphans`), so a second instance's
pass logs that it stood by and reports zero. What each pass guarantees on retry:

- **Inspection** commits each file on its own. A file that gets no verdict is deferred — one minute,
  doubled per attempt, at most one hour — and queues behind the files that have waited longer, so
  every deposited file is offered again at most an hour after its last attempt and none holds the
  queue. An outage never releases or refuses a file.
- **The abandonment purge** is idempotent: a failed pass leaves rows the next pass selects again.
- **Reclamation** walks the keys in order from a checkpoint saved after each page and reset once the
  end is reached: every object present when a walk starts is visited within
  ⌈objects / 250,000⌉ + 1 passes, across restarts. With the `FileSystem` store it also deletes
  temporary files a crashed write left behind, once older than a day.

### `Files`

The Files feature's application policy: the categories a file is registered under, and the ceiling
of an upload carried by the API. Bound by `AddFiles` and validated at start-up.

| Key | Type | Default | Notes |
|---|---|---|---|
| `ApiUploadMaxBytes` | long | `10485760` (10 MiB) | Largest file uploaded through the API (`POST /api/v1/files/uploads`). That endpoint alone accepts a body of this plus 16 KiB of multipart framing, above `RequestLimits:MaxRequestBodyBytes`; a larger body is `413`, a larger file `400`. Range 1 KiB–30 MiB. |
| `Categories:<name>:AllowedMediaTypes` | string[] | — | **At least one.** Exact `type/subtype`, compared ignoring case; no wildcard, no parameters. |
| `Categories:<name>:MaxSizeInBytes` | long | — | **Required.** 1 byte to 5 GiB. |
| `Categories:<name>:Scan` | bool | `false` | Send the content to the malware scanner. Refused at start-up while `Files:Scanning:Enabled` is false. |
| `Categories:<name>:VerifySignature` | bool | `true` | The leading bytes must agree with the declared type. Markup that can carry a script is refused either way. |
| `Categories:<name>:Visibility` | string | `Private` | `Private`: only the owner downloads. `Authenticated`: any signed-in user downloads an *available* file by its id. |
| `Categories:<name>:UploadModes` | string[] | — | **At least one** of `Direct` (signed URL to the store) and `Api`. A category that allows only `Direct` is refused with the `FileSystem` provider. |
| `Categories:<name>:Quota:Mode` | string | `Strict` | `Strict` or `Indicative`, compared ignoring case. See below. |
| `Categories:<name>:Quota:MaxFiles` | int | `1000` | Files one owner may keep in this category, in any state. |
| `Categories:<name>:Quota:MaxBytes` | long | `10737418240` (10 GiB) | Bytes one owner may hold in this category, pending ones counted at their declared size. |
| `Categories:<name>:Quota:MaxPendingUploads` | int | `20` | Registrations one owner may have waiting for their bytes in this category. |

**A category name is a lower-case slug** (`a-z`, `0-9`, `-`, 1–64 characters), and a file keeps
the one it was registered under. At least one category must be declared. The shipped
`appsettings.json` declares `document` (PDF, CSV, plain text, 50 MiB, private) and `image` (PNG,
JPEG, WebP, 10 MiB, readable by any signed-in user), both unscanned because scanning is off by
default.

**Quotas are per owner and per category**, in one of two modes. The shipped categories use `Strict`.

- `Strict`: the usage read, the row insert and its commit run under a PostgreSQL advisory lock keyed
  by (owner, category), so the quota holds under concurrency across every host sharing the
  database. The cost: one extra, unpooled database connection held for the registration's duration,
  and the registrations of one owner in one category run one at a time. A registration that waits
  more than 10 seconds for the lock is refused with `409` `storedFile.quotaBusy` and writes nothing.
- `Indicative`: usage is read, then the row inserted, with no lock. A burst of N concurrent
  registrations of one owner in one category can overshoot by at most N − 1 files and their
  declared bytes.

Deletions, the abandoned-registration purge and inspection never take the lock: they only reduce
usage or leave it unchanged.

**Removing a category does not orphan its files.** They stay readable by their owner, are treated
as `Private`, and a file still waiting for inspection is inspected strictly (scanned when scanning is
enabled, signature verified). No new file can be registered under it.

**To scan a category**, enable [`Files:Scanning`](#filesscanning) first, then set its `Scan` to
`true`; the order matters only because start-up refuses the second without the first.

### `Files:Storage`

Which store holds the Files feature's bytes. One choice, read at start-up; the chosen provider's own
section below is the only one bound and validated.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Provider` | string | `S3` | `S3` or `FileSystem`. Anything else is refused at start-up. |

**The two providers differ in who carries the bytes.** `S3` signs URLs: a client deposits on the
store and is redirected to it to download, and no file byte passes through the API. `FileSystem`
cannot sign: registering a direct upload answers `409` `storedFile.directUploadUnavailable`, and
`GET /files/{id}/content` streams the bytes from the API (`200`) instead of redirecting (`302`).
With `FileSystem`, start-up refuses a category whose only upload mode is `Direct`.

### `Files:Storage:FileSystem`

A directory on a local or mounted disk, when `Files:Storage:Provider` is `FileSystem`.

| Key | Type | Default | Notes |
|---|---|---|---|
| `RootPath` | string | `""` | **Required** for this provider, and **absolute**. Created on first write. Exclusive to this feature — the orphan sweep deletes every file under it that no row names. |

An object is the file at `RootPath/<object key>`; a client-supplied file name never reaches the disk,
and a key that would resolve outside the root is refused. A write goes to a temporary file in the
same directory while it is counted and hashed, and is published by a rename only when its size and
SHA-256 match the declaration. Describing an object reads it whole to compute its digest.

### `Files:Storage:S3`

The bucket, when `Files:Storage:Provider` is `S3`. Bound by `AddStorageModule` and validated at
start-up — only for that provider — so a bucket nobody can sign for stops the process instead of
failing on the first upload.

| Key | Type | Default | Notes |
|---|---|---|---|
| `BucketName` | string | `""` | **Required.** One bucket, exclusive to this feature — the orphan sweep deletes anything in it that no row references. |
| `Region` | string | `us-east-1` | |
| `Endpoint` | string | `""` | Empty means AWS S3 itself. Set it for SeaweedFS, MinIO, R2, or any S3-compatible store. |
| `PublicEndpoint` | string | `Endpoint` | The name a **client** resolves, when it is not the one this process uses. |
| `ForcePathStyle` | bool | `false` | Required for a store addressed by host and port, like the local SeaweedFS: virtual-host addressing would resolve `<bucket>.objectstore`, which nothing serves. |
| `AccessKeyId` | string | `""` | Leave **empty in production** — see below. |
| `SecretAccessKey` | string | `""` | Same. Supplying exactly one of the pair is refused. |
| `AllowInsecureTransport` | bool | `false` | Only a plain-HTTP local endpoint has business setting this. |
| `MaxGrantLifetime` | TimeSpan | `00:30:00` | How long a signed upload or download URL stays valid. |

**`PublicEndpoint` exists because a presigned URL covers the host it was signed for.** Measured on
this repository: sign for `127.0.0.1:19000`, follow the URL as `localhost:19000` — same machine,
same port, same server — and the store answers `403 SignatureDoesNotMatch`. So nothing downstream
can rewrite the host: not the API, not a proxy, not the client. The name has to be right at signing
time, which is what this key is.

It matters wherever the store has two names. In `docker-compose.yml` the API reaches the store at
`objectstore:9000` on the Docker network while a browser reaches it at `localhost:9000`; behind a
private service or a self-hosted gateway it is the same shape. The case where the need disappears
is AWS S3 itself, where `Endpoint` is empty. Left unset it defaults to `Endpoint`, so a deployment
that never knew the key behaves exactly as before.

**Two of the three validation rules follow the *public* endpoint, and that is the point.** The
scheme of the signed URL, and the refusal to send a bearer right in clear without
`AllowInsecureTransport`, are properties of the endpoint that produces URLs. The typical deployment
is plain HTTP inside the mesh and TLS at the ingress; validating against the *internal* endpoint
would force such a deployment to allow insecure transport for the internal hop and then sign
`http://` URLs for a public that is on `https://`. An internal endpoint in clear carries no
reusable credential (Signature V4 puts a per-request signature on the wire, never the secret) —
though it does carry the bytes the inspection pass reads, inside the network the operator chose; a
**public** endpoint in clear puts a reusable right to read or write an object into a proxy log, a
`Referer` and a browser history.

**Leave the two credentials empty.** Empty is not "unconfigured", it is the intended production
shape: the AWS SDK's own credential chain resolves an instance role, and no long-lived key pair
exists to leak. Set them only for a local store, where `docker-compose.yml` already does it: `STORAGE_ACCESS_KEY_ID` and
`STORAGE_SECRET_ACCESS_KEY` in `.env` are both the identity the `objectstore` service is created with
and the pair the API signs with, so the two cannot disagree.

**`MaxGrantLifetime` is a security setting wearing a duration.** A signed URL is a bearer right for
as long as it lives — anyone who has it can read the object, with no further check. Shorten it
rather than lengthen it, and see `SECURITY.md`.

**The bucket is exclusive to this feature.** The orphan sweep reclaims bytes by difference — it
lists the bucket and deletes every object no row names — so anything else stored there is deleted on
the next pass.

### `Files:Scanning`

Whether deposited files are scanned for malware by a `clamd` daemon. Entirely optional: disabled,
the process starts normally and the type check on a file's leading bytes still runs in every
category that verifies signatures. Which files are scanned is each category's `Scan`.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | `false` registers a scanner that finds nothing. |
| `Host` | string | `""` | **Required when `Enabled` is true.** |
| `Port` | int | `3310` | clamd's default. Validated only when scanning is enabled. |
| `MaxScannableBytes` | long | `26214400` (25 MiB) | Must stay at or below the daemon's own `StreamMaxLength`. Larger content is refused as not inspectable. |

**ClamAV speaks its own TCP protocol, not HTTP**, so the outbound resilience policy does not cover
it — the same asymmetry the S3 SDK has. Its timeouts are set on the adapter by hand to agree with
that budget.

**Inspection is the only thing that makes a file readable**, so
`Jobs:Files:InspectDepositedFilesInterval` is a latency a user feels, not a background cost — and
`Jobs:Files:InspectDepositedFilesEnabled` set to `false` does not degrade the feature, it stops it.
That is why the two live under `Jobs:Files` with the sweeps but are documented as the opposite kind
of switch.

<!--#endif-->
### Outbound HTTP — deliberately not configurable

There is no section for it, and that is a decision rather than an omission. There is one policy,
written once in
`Src/Presentation/AppTemplate.Presentation.Core/Common/Outbound/OutboundHttpExtensions.cs`, and each
host installs it with `AddOutboundHttp()` on `IHttpClientFactory`'s defaults — so every typed client
any module registers gets it, in every host that composes that module:

| | Value |
|---|---|
| Timeout per attempt | 10 s |
| Timeout for the whole call, retries included | 30 s |
| Retries | up to 3, exponential, with jitter, **on GET/HEAD/OPTIONS/TRACE only** |
| Circuit breaker and concurrency limiter | the standard handler's defaults |

These are guard rails, not tuning knobs. The 30 s total is chosen **against**
`RequestTimeouts:Default`, which gives an inbound request 5 minutes: an outbound call happens inside
an inbound one, so the enclosing budget has to be the longer of the two — the same rule
`RequestTimeoutsOptions` states about the layer beneath it, applied one level up. A factor of ten
leaves room for a request that calls several dependencies in turn. Exposing these as configuration
would let an operator break that relationship with nothing to notice, so the reasoning lives in the
code beside the numbers instead. A deployment that genuinely needs different values is editing a
template it already owns.

Retry is an allow-list of the **safe** verbs. PUT and DELETE are idempotent by specification and
still excluded, because that promise belongs to the server at the other end and a default applies to
servers nobody here controls — and replaying a large PUT because the response was slow doubles the
bytes for nothing.

Nothing in this template calls outwards yet. The policy is installed anyway so that the first
adapter that does is not the one deciding what a timeout is, and two architecture rules keep the two
escapes shut: a client built with `new` never meets the factory, and a host that never installs the
defaults gives every client in it no budget at all.

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

<!--#if (localAuth)-->
### `Jwt`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Key` | string | `""` | **Required.** HS256 signing key, **minimum 32 bytes**. Secret — user secrets or env var only. Outside Development, a key starting with `dev-only` — the public one `.env.example` and `appsettings.Development.json` ship — is refused at start-up. |
| `Issuer` | string | `""` | **Required.** Issuer validation is never disabled. |
| `Audience` | string | `""` | **Required.** Audience validation is never disabled. |
| `RequireHttpsMetadata` | bool | `true` | Only a local host has business setting `false`. |
| `AccessTokenLifetimeInMinutes` | int | `15` | Range **1–1440**. Short on purpose: an access token cannot be revoked. |

### `RefreshToken`

| Key | Type | Default | Notes |
|---|---|---|---|
| `LifetimeInDays` | int | `7` | Range **1–90**. Token size (32 bytes) and hashing (SHA-256) are not configurable. |
| `RetentionInDays` | int | `7` | Range **1–90**. How long an expired grant's row survives before `ISessionTokensService` deletes it — a retention window on rows, not a lifetime on tokens. Read by the sweep that runs in the maintenance job. |

### `SecurityEventLog`

How long the persisted security-event trail — every sign-in attempt, lockout, credential
revocation and replay the `ILogger` output already carries — is kept before the purge deletes it,
readable over the API by the account holder (`GET /auth/me/security-events`) and by an
administrator for any account.

| Key | Type | Default | Notes |
|---|---|---|---|
| `RetentionInDays` | int | `90` | Range **1–3650**. Days an event is kept before `PurgeExpiredSecurityEvents` deletes it — for every account, a deleted one included, whose rows are removed by this purge and nothing else. Events also survive the account itself: the table carries no foreign key, by design. |
| `PurgeBatchSize` | int | `1000` | Range **1–100000**. Rows one delete statement removes; the purge repeats until a statement removes fewer, so a large backlog is cleared in short transactions rather than one long lock over the table. |

### `Identity`

Password, lockout and sign-in policy. Every member has a safe default, so a
partly-filled section neither tightens nor loosens anything unexpectedly.

**This section is the only password policy.** ASP.NET Identity applies it to every new
password, at registration, password change and password reset, and its refusals come back on
the password field as `request.validationFailed`. The request validators check nothing more
than presence and a ceiling of 256 characters, which bounds the hashing work one request can
cause; `PasswordRequiredLength` may not exceed that ceiling.

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

### `TwoFactor`

The second factor's own settings. Every member has a safe default, so an absent section
validates cleanly and behaves exactly as the tracked `appsettings.json` states.

| Key | Type | Default | Notes |
|---|---|---|---|
| `ChallengeLifetime` | timespan | `00:05:00` | Must be between **1 and 30 minutes**. This is what stands between a verified password and a token pair, and unlike a refresh token nobody is expected to hold on to it. |
| `RecoveryCodeCount` | int | `10` | Must be **1–20**. How many single-use codes confirmation mints, shown once and never again. |
| `MaxChallengeAttempts` | int | `5` | Must be **1–20**. Wrong codes one challenge tolerates before it is destroyed and the password has to be presented again. **This is the only bound on guessing a code:** account lockout counts failed *password* checks and presenting a code is not one, so raising this is a security decision, not a convenience. |
| `Issuer` | string | `AppTemplate` | Must not be blank. **The label an authenticator app shows next to the account** — a user-visible string, so rename it with every other `AppTemplate` occurrence a fork replaces. |

`SECURITY.md` describes the challenge as short-lived and the codes as ten; both of those are
this section, not constants.

**The three values are one budget.** A challenge lives `ChallengeLifetime` and tolerates
`MaxChallengeAttempts` wrong codes, and the rate limiter admits 10 requests a minute per client
address — per process, so times your replica count. Multiply the three before raising any of them:
what they bound together is how many six-digit codes somebody who already has the password may
offer.

### `Passkey`

The relying-party side of the passkey ceremonies, and how long a ceremony's state lives. The rest
of the WebAuthn posture — user verification required, the challenge size, the authenticator
timeout — is ASP.NET Core Identity's own safe defaults, kept deliberately unconfigurable.

| Key | Type | Default | Notes |
|---|---|---|---|
| `ServerDomain` | string | *(the request's host)* | The WebAuthn relying-party id: the effective domain of this API, which a browser's `navigator.credentials.*` calls require their origin to be a subdomain of. A bare domain — no scheme, port or path. **Changing it invalidates every passkey the accounts hold**, because a credential is scoped to the domain it was created under; set it once, before the first registration. |
| `CeremonyLifetime` | timespan | `00:05:00` | How long the `registrationState` / `authenticationState` an options call hands out stays redeemable. Must be between **1 and 30 minutes**. The default is the authenticator timeout Identity writes into the options: shorter refuses a ceremony the browser may still finish, longer only widens the window for a captured state. |

The state is sealed by ASP.NET Core data protection — the key ring the identity schema already
persists, so every instance can open a state another issued — under a purpose per ceremony kind,
and expires after `CeremonyLifetime`. Each state is spent on its first presentation: a row in
`identity.SpentPasskeyCeremonies`, keyed by a hash of the state, kept until the state expires plus
five minutes for clock skew between instances. There is no switch to purge that table: every
options call deletes the rows past that point, so it holds a few minutes of ceremonies and needs no
maintenance task.

<!--#endif-->
### `ProblemTypes`

| Key | Type | Default | Notes |
|---|---|---|---|
| `BaseUri` | string | `https://apptemplate.example/problems` | Must be an absolute URI. Prefixes the `type` of every RFC 7807 body, so it is one of the values a fork changes before it publishes anything: the default names a domain it does not own. |

### `Email`

SMTP transport.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Transport` | string | `Smtp` | `Smtp` or `Postmark`. It decides whether the rest of this table is read at all — see [Email:Transport](#emailtransport--smtp-or-an-http-api) above. |
| `Host` | string | `""` | **Required when `Transport` is `Smtp`.** |
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

<!--#if (localAuth)-->
### `EmailConfirmation`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConfirmEmailUrl` | URI | — | **Required.** Absolute `http`/`https` URL of the page that completes confirmation, with **no fragment**. Must be browser-reachable, not a container name. |

The email address and single-use token are appended as a URL **fragment**, which
browsers never transmit — so the token stays out of server access logs, `Referer`
headers and intermediary request history. The page reads the fragment and **POSTs** it
to `/api/v1/auth/confirm-email` as a JSON body. Confirmation is not a GET with a query
string, for the same reason.

### `IdentityTokens`

The lifespan of a token minted by ASP.NET Identity's **default** data-protector token
provider — which is to say email confirmation's, and any purpose added later that does
not register a provider of its own. `PasswordReset` and `EmailChange` each have one, so
neither is governed by this section; see the note under `PasswordReset` below.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Lifespan` | timespan | `1.00:00:00` | Must be between **5 minutes and 30 days**. This is the framework's own one-day default, stated here rather than inherited silently. |

**It is a day where the other two are an hour**, and that asymmetry is worth a decision
rather than a discovery: a confirmation link is mailed to an address nobody has proved
yet, so it is the one token whose window is bounded by how long a person takes to open
their mail. Shorten it if your users confirm promptly; the two shorter windows below are
not evidence that this one was meant to match them.

### `PasswordReset`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ResetPasswordUrl` | URI | — | **Required.** Absolute `http`/`https` URL of the page that completes the reset, with **no fragment**. Must be browser-reachable, not a container name. |
| `TokenLifespan` | timespan | `01:00:00` | Must be between **5 minutes and 1 day**. Its own value, deliberately not shared with `EmailConfirmation`'s token lifespan — [`DECISIONS.md`](DECISIONS.md#authentication) says why one shared lifespan across every token provider would be wrong here. |

Same fragment-then-POST shape as `EmailConfirmation`, and for the same reason: the email
and single-use token are appended as a URL fragment, and the page **POSTs** them to
`/api/v1/auth/reset-password` as a JSON body.

### `EmailChange`

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConfirmEmailChangeUrl` | URI | — | **Required.** Absolute `http`/`https` URL of the page that completes the change, with **no fragment**. Must be browser-reachable, not a container name. |
| `TokenLifespan` | timespan | `01:00:00` | Must be between **5 minutes and 1 day**. Its own named token provider, same reasoning as `PasswordReset:TokenLifespan` above. |

Same fragment-then-POST shape again: the new address and single-use token are appended
as a URL fragment, and the page **POSTs** them to `/api/v1/auth/confirm-email-change` as
a JSON body.

**A mail's subject is not configured.** It is the `<title>` of the template that renders its
body, so the two are one file and cannot end up in different languages — which is exactly what
happened while the subject lived here: an English `Subject` was delivered above a French body for
as long as nobody opened the mail. See [`Localization`](#localization).

**All three of `EmailConfirmation`, `PasswordReset` and `EmailChange` are validated at startup.**
All three are bound by `AddAuthModule`, not by anything HTTP-specific, so a process that composes that
module — for the account routes and the refresh-token purge, see
[above](#one-process-http-and-the-periodic-jobs) — requires all three URLs.

<!--#endif-->
### `Localization`

The language a mail is written in. Bound from the one options class in
<!--#if (localAuth && reminders)-->
`Src/Presentation/AppTemplate.Presentation.Core/Common/Localization/LocalizationOptions.cs`, so a
deployment cannot write a password reset in one language and a reminder in another.
<!--#else-->
`Src/Presentation/AppTemplate.Presentation.Core/Common/Localization/LocalizationOptions.cs`, so a
deployment has one default language for every mail it writes.
<!--#endif-->

`AddLocalizationOptions()` binds and validates the section and does nothing else. Reading the value
into `CurrentLanguage.Default` is the host's own call, because *when* that has to happen is a host's
question: `AppTemplate.Api` does it as the request pipeline is built, inside `UseRequestLanguage`,
which is before the first job pass runs, so every mail a job sends is written in the configured
default. A process with no pipeline would do it once the container exists and before the first loop
runs. A blank or malformed tag fails startup either way with the same message.

| Key | Type | Default | Notes |
|---|---|---|---|
| `DefaultCulture` | string | `en` | A culture name the runtime knows — `en`, `fr`, `fr-CA`. Startup fails on one it does not, rather than falling back silently for the life of the deployment. |

**There is deliberately no list of supported languages.** What a mail can be written in is which
templates are embedded, and a list here would be a second statement of that — free to name a
language no template backs.
<!--#if (localAuth && reminders)-->
Adding a language is adding
`<Mail>.<culture>.html` next to the ones that ship, in
`AppTemplate.Infrastructure.Auth/Features/Accounts/Resources/Templates/` (the three account mails) and
`AppTemplate.Infrastructure.Email/Features/Reminders/` (the reminder), and nothing else.
`EmailTemplateCoverageTests` refuses a language added to one of those folders and not the other.
<!--#elseif (localAuth)-->
Adding a language is adding
`<Mail>.<culture>.html` next to the ones that ship, in
`AppTemplate.Infrastructure.Auth/Features/Accounts/Resources/Templates/` (the three account mails), and nothing else.
<!--#elseif (reminders)-->
Adding a language is adding
`<Mail>.<culture>.html` next to the ones that ship, in
`AppTemplate.Infrastructure.Email/Features/Reminders/` (the reminder), and nothing else.
<!--#endif-->

**How a reader's language is chosen.**

- In `AppTemplate.Api`, from the request's `Accept-Language`, by `UseRequestLanguage`. What it sets
  is `CurrentLanguage.Tag` — a BCP-47 string, not a `CultureInfo`. The repository builds with
  `InvariantGlobalization=true` and the API's runtime image ships no ICU because of it, so
  `CultureInfo.GetCultureInfo("fr")` throws at run time and `CurrentUICulture` is not a thing this
  template can set. The tag decides which text is chosen and nothing else: how a value parses stays
  invariant everywhere, which is what keeps one body from meaning different things to different
  callers. `CurrentLanguage`'s own remarks carry the reasoning.
- A request that names no language, and every mail a background job sends, uses
  `DefaultCulture`. A job serves no request, so it has no reader to ask.
- A language with no template falls back to English, which every template family must ship.

<!--#if (localAuth && reminders)-->
**To follow a stored per-account preference instead**, add the column to `AppUser`, carry it on
`UserProfile`, and set `CurrentLanguage.Tag` from it — in `EmailReminderNotifier` for the reminder
job's mail, and ahead of the use case for the API's, where the request header has already set it and the
stored preference would win. That is the one change that would make a reminder follow its reader
rather than the deployment; nothing else here would move.

<!--#elseif (localAuth)-->
**To follow a stored per-account preference instead**, add the column to `AppUser`, carry it on
`UserProfile`, and set `CurrentLanguage.Tag` from it ahead of the use case, where the request header
has already set it and the stored preference would win.

<!--#elseif (reminders)-->
**To follow a stored per-account preference instead**, keep it where the reminder's recipient is
resolved and set `CurrentLanguage.Tag` from it in `EmailReminderNotifier`. That is the one change that
would make a reminder follow its reader rather than the deployment; nothing else here would move.

<!--#endif-->
<!--#if (localAuth)-->
### `IdentitySeed` — development only

Creates an administrator account at startup.

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `false` | Opt-in. Additionally, `IdentitySeeder` **throws** if this is true outside the Development environment. |
| `AdminEmail` | string | `""` | Required when enabled. The administrator's only identifier: it signs in with this address. |
| `AdminPassword` | string | `""` | **Required when enabled, and has no default.** Supply via user secrets or an env var. |

> **Warning.** Enabling this creates a privileged account with the `Admin` role and a
> pre-confirmed email address. It is off by default, refuses to run outside
> Development, and enabling it with a blank password **fails startup** rather than
> creating a guessable administrator. Never enable it in an environment reachable from
> the internet.

Note that seeding only runs from the Development startup path
(`MigrateAndSeedForDevelopmentAsync`), so setting `Enabled=true` in another
environment fails at that call site rather than being quietly ignored.

<!--#endif-->
### `Cors`

| Key | Type | Default | Notes |
|---|---|---|---|
| `AllowedOrigins` | string[] | `[]` | Exact origins. Bind by index from the environment: `Cors__AllowedOrigins__0`, `…__1`, … |

`CorsOptionsValidator` refuses an entry that is not a well-formed origin — a trailing slash, a
path, a hostname with no scheme or a scheme other than `http`/`https` — and the host fails
`ValidateOnStart()` and does not boot, since such an entry would otherwise match no browser request
without any error. A lone `*` is accepted and means any origin.

- **An empty or absent array allows nothing**, rather than silently allowing
  everything. A same-origin caller is unaffected, since CORS only governs
  cross-origin requests.
- When origins are configured, the policy allows any header and any method, exposes
  `Retry-After`, `ETag`, `Location` and `Idempotency-Replayed`, and caches preflight for
  10 minutes. Those four are exactly the response headers this API's contracts ask a client
  to read, and a browser cannot see one that is not exposed.
<!--#if (accounts)-->
- `AllowCredentials` is deliberately **not** set: tokens travel in the `Authorization`
  header, not a cookie. It is the wildcard-origin-plus-credentials combination that
  turns a permissive policy into a vulnerability.
<!--#else-->
- `AllowCredentials` is deliberately **not** set. It is the wildcard-origin-plus-credentials
  combination that turns a permissive policy into a vulnerability.
<!--#endif-->

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

<!--#if (localAuth)-->
- **Leave it off and the rate limiter partitions on the proxy's address**, which means every
  caller on earth shares one 10-request window and the brute-force protection does nothing.
<!--#else-->
- **Leave it off and the rate limiter partitions on the proxy's address**, which means every
  caller on earth shares one `GlobalPermitLimit` window and the limiter protects nothing.
<!--#endif-->
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
| `ServiceName` | string? | `null` | Falls back to the calling host's own assembly name and informational version — see below for why it has to be the host's. Present but blank is refused; remove the key instead. |
| `TracesSamplingRatio` | double | `1.0` | Must be **greater than 0 and at most 1**. Not in any tracked `appsettings.json`; the default samples every trace, which is what a template should do until ingestion volume, rather than curiosity, argues for less. `NaN` is refused. |

Bound and validated once, by `AddObservability` in
`Src/Presentation/AppTemplate.Presentation.Core/Common/Observability/ObservabilityExtensions.cs`,
which every host calls, so a sampling ratio set for a deployment means the same thing in each of its
processes, should it ever run more than one kind. What that call registers is everything two hosts
disagreeing about would be a bug rather than a difference: the resource, the sampler, the outbound-`HttpClient` instrumentation, the
`System.Runtime` runtime meters and the OTLP exporter. With `Enabled` left false it registers none
of it — no instrumentation, no exporter, no background flush.

**Three things it takes from the calling host instead of deciding**, which is how one registration
still gives each host its own spans. Each is forced rather than preferred:

- **The service name.** The call takes the host's `Assembly`, not a string: `service.name` and
  `service.version` are read off it, so a rename or a version bump cannot leave a stale literal
  behind. It cannot read its own assembly, because two hosts would then announce themselves under
  one name, and two processes reporting as one is worse than either reporting nothing. `ServiceName`
  above overrides the result when several deployments share one collector.
- **The diagnostics names.** A host's `ActivitySource` and `Meter` names are constants on classes in
  its own feature folders, so a shared project that listed them would have to reference the host
  that references it. They arrive through two callbacks, one for tracing and one for metrics,
  invoked before the exporter so a host can still add a processor of its own.
- **The ASP.NET Core and Npgsql instrumentation**, neither of which the transport-agnostic project
  can carry: the first needs the ASP.NET framework reference that project deliberately refuses, and
  the second pulls the PostgreSQL driver, which would put a database behind every host that wanted
  traces.

So the registration is two storeys, and each storey adds what the one below it cannot know.
`Src/Presentation/AppTemplate.Api/Common/Observability/ObservabilityExtensions.cs` adds the
ASP.NET Core instrumentation, the `Microsoft.AspNetCore.RateLimiting` meter — named there because
the rate limiter is registered there, so a mechanism and its one diagnostic travel together — the
exclusion of `/health*` from both traces and the request log, and what only this host knows:
`AddNpgsql()`, the `Npgsql` meter, and its own assembly, from which `service.name` is read.
Together with the shared registration, traces cover ASP.NET Core, `HttpClient` and Npgsql, and
metrics cover ASP.NET Core, `HttpClient`, the runtime, the connection pool and the rate limiter.
`Src/Presentation/AppTemplate.Api/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` adds the
jobs' own sources and meters to that same pipeline —
<!--#if (reminders)-->
the jobs' `ActivitySource`s and `Meter`s and
the persistence project's reminder meter, by `PersistenceModule.RemindersMeterName` —
<!--#else
the jobs' `ActivitySource`s and `Meter`s —
<!--#endif-->
through
callbacks that run only when telemetry is enabled and the jobs run, so an instance with no collector,
or with `Jobs:Enabled=false`, registers nothing for them. `service.name` is `AppTemplate.Api`,
read from its assembly, for the jobs as for requests.

**The jobs' signals** are what tells a stopped loop from a quiet one. Each pass adds one to an
`iterations` counter tagged with its `outcome` (`success`, `failure`, `exception` or `disabled`) and
its `task`, whether or not it found anything, and writes a log line on start, stop and every skip; a
flat `iterations` series is a loop that stopped, and `outcome != "disabled"` is what an alert on that
should read. The meters are named after their section, one per loop —
<!--#if (files && reminders)-->
`AppTemplate.Jobs.Files`, `AppTemplate.Jobs.Maintenance` and `AppTemplate.Jobs.Reminders` —
<!--#elseif (files)-->
`AppTemplate.Jobs.Files` and `AppTemplate.Jobs.Maintenance` —
<!--#elseif (reminders)-->
`AppTemplate.Jobs.Maintenance` and `AppTemplate.Jobs.Reminders` —
<!--#else
`AppTemplate.Jobs.Maintenance` —
<!--#endif-->
and their counters sit under `apptemplate.jobs.*`.

- **An unreachable collector is safe.** Startup succeeds and the failing export cycles produce
  no log output at all, because the OTLP exporter reports failures on its own `EventSource`
  rather than through `ILogger`. Verified against a dead endpoint.
- The `traceId` in a ProblemDetails body is `HttpContext.TraceIdentifier`. The request log
  records both that and the W3C `TraceId`, and the span is tagged with the `TraceIdentifier`, so
  a caller's `traceId` joins to the log entry and the log entry joins to the trace — from either
  end.
- **The request log never enumerates headers, reads cookies, touches the body, or logs the query
<!--#if (localAuth)-->
  string.** It writes a fixed field list. The auth endpoints carry passwords and refresh tokens
  in their bodies, so body logging is not merely disabled but absent. If you add logging of your
  own, this guarantee does not extend to it.
<!--#else-->
  string.** It writes a fixed field list, so body logging is not merely disabled but absent. If you
  add logging of your own, this guarantee does not extend to it.
<!--#endif-->

### `Concurrency`

| Key | Type | Default | Notes |
|---|---|---|---|
| `IfMatch` | `Optional` \| `Required` | `Optional` | `Required` refuses a mutating request with no `If-Match` header with `428`. |

Read by `Src/Presentation/AppTemplate.Api/Common/Concurrency/ConcurrencyExtensions.cs`. Every read that
names one versioned resource publishes a strong `ETag` regardless of this setting, and every write
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
| `Retention` | timespan | `1.00:00:00` (24 hours) | Sets each row's `ExpiresAt` — how long a *completed* response stays replayable. Must be > 0 and ≤ 30 days. See the note below on what actually enforces it. |
| `ClaimLease` | timespan | `00:15:00` | How long an *unfinished* claim blocks a retry before it is treated as abandoned and made reclaimable. Must be > 0 and ≤ `Retention`. |
| `MaxKeyLength` | int | `128` | Must be 1…512. A longer key is `400` `idempotency.keyInvalid`. |
| `MaxStoredResponseBytes` | int | `8192` | Must be ≥ 1. A larger response is stored without its body, and a replay then answers `409` `idempotency.notReplayable` rather than a truncated body. |

Read by `Src/Presentation/AppTemplate.Api/Common/Idempotency/IdempotencyExtensions.cs`. The filter is
<!--#if (anyFeature)-->
registered globally but is **inert unless the action carries `[Idempotent]`** — these do:

<!--#if (todoLists)-->
- `POST /api/v1/todo-lists`
- `POST /api/v1/todo-lists/{id}/items`
<!--#endif-->
<!--#if (reminders)-->
- `POST /api/v1/todo-lists/{id}/items/{id}/reminders`
<!--#endif-->
<!--#if (notes)-->
- `POST /api/v1/notes`
<!--#endif-->
<!--#if (files)-->
- `POST /api/v1/files`
<!--#endif-->

Sending no `Idempotency-Key` is always allowed: the capability is available, not compulsory.
<!--#else-->
registered globally but is **inert unless the action carries `[Idempotent]`**, and no action of this
project carries it yet: a feature marks its own creating routes. Sending no `Idempotency-Key` is
always allowed: the capability is available, not compulsory.
<!--#endif-->

What each setting does when it is wrong:

- **`Retention` is a stamp, not a timer.** It only decides the `ExpiresAt` written on each row.
  Expiry is enforced by the maintenance job's idempotency purge
  (`Jobs:Maintenance:PurgeExpiredIdempotencyKeysEnabled`), on the instance that runs the jobs.
<!--#if (accounts)-->
  `DELETE /api/v1/maintenance/idempotency-keys/expired` runs the same purge for a scheduler outside
  the process; it requires the `Administrator` policy, and is the template's worked example of
  policy-based authorisation.
<!--#endif-->
  Until a purge runs, a completed key stays replayable past its retention, and the table grows.
- **`Retention` too short** (with purging in place) — a client retrying after a network stall past
  the window creates a second resource, which is the exact failure the feature exists to prevent.
- **`ClaimLease` is unrelated to `Retention`.** `Retention` governs how long a *completed* response
  stays replayable; `ClaimLease` only ever matters while a claim is still unfinished — it is what lets
  a retry take a key back from a claimant whose process died (a killed pod, an OOM) between claiming
  it and calling `CompleteAsync` or `ReleaseAsync`. Without it that key would answer `409`
  `idempotency.inProgress` for the rest of its 24-hour retention instead of minutes.
- **`ClaimLease` too short** — a lease shorter than the platform's own `RequestTimeouts:Default`
  ceiling (5 minutes by default) can expire while a slow-but-legitimate write is still in flight,
  handing the same key to a second, concurrent retry: the exact double write this mechanism exists to
  prevent. The shipped default (15 minutes) is that ceiling plus headroom for clock drift and the
  little work left after the action returns; shrink `RequestTimeouts:Default` before shrinking this.
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
<!--#if (files)-->
- **One endpoint raises it**: `POST /api/v1/files/uploads` accepts `Files:ApiUploadMaxBytes` plus
  16 KiB, applied to the `Content-Length` check, to Kestrel's limit for that request and to the
  multipart body. Every other endpoint keeps this value.
<!--#endif-->
- **Set too low** and legitimate requests start failing with `413`; the validator's 1024-byte floor
  stops the value going so low that nothing works at all.
- **Set outside the allowed range** and the host fails `ValidateOnStart()` and does not boot.

### `Shutdown`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Timeout` | timespan | `00:00:30` | Must be greater than zero and at most 10 minutes. |

Read by `Src/Presentation/AppTemplate.Api/Common/Hosting/HostLifecycleExtensions.cs`, which applies it to
the framework's own `HostOptions.ShutdownTimeout` — how long the host waits for in-flight requests
to drain once it starts stopping. A deployment must allow enough time after signalling shutdown
for this timeout and any delay while traffic routing catches up.

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

Read by `Src/Presentation/AppTemplate.Api/Common/Hosting/HostLifecycleExtensions.cs`, which installs
`AddRequestTimeouts`/`UseRequestTimeouts` with this one default policy. A response still not started when
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

**An endpoint whose normal work legitimately runs longer than `Default`** (a bulk import, say) names
its own policy with `[RequestTimeout]`; **a streaming endpoint (SSE, an `IAsyncEnumerable` response)
must use `[DisableRequestTimeout]` instead, never a larger number.** Once the first byte is flushed
there is no channel left to report a timeout on, so a "very large" value is not safer, only later —
it is still reached eventually, at the worst possible moment, and produces a silent cutoff instead
of a clean error either way.

**Set `Default` too low** and any endpoint whose work legitimately nears the numbers above starts
failing under normal load, not just under a real incident. **Set it outside 1 second – 1 hour** and
the host fails `ValidateOnStart()` and does not boot.

### `Jobs`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Enabled` | bool | `true` | Whether this instance runs the periodic jobs at all. `false` still serves HTTP; no pass runs and no job section is bound or required. |

A role switch, read by `AddBackgroundJobs` before it registers anything, so it is not an
`IValidateOptions` rule — a value that is not a boolean fails startup from the binder. See
[One process](#one-process-http-and-the-periodic-jobs) for running several instances.

### `Jobs:Maintenance`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Interval` | timespan | `01:00:00` | How often the maintenance job wakes up. Governs every task. Range **1 second – 1 day**. |
| `PurgeExpiredIdempotencyKeysEnabled` | bool | `true` | Runs `IPurgeExpiredIdempotencyKeysUseCase` each iteration when `true`. |

Read by `AppTemplate.Api/Features/Maintenance/Jobs/JobsMaintenanceOptions.cs`, when
`Jobs:Enabled` is true. Each task can be switched off independently — an operator running
<!--#if (localAuth)-->
the idempotency purge here but one of authentication's some other way
<!--#else
the idempotency purge here
<!--#endif-->
is not forced into the rest.
<!--#if (localAuth)-->
The two authentication switches are documented next, because they belong to authentication.
<!--#endif-->

- Every iteration resolves its use cases from one fresh background scope
  (`IServiceScopeFactory.CreateBackgroundScope()`), never from a scope held for the process lifetime,
  so no use case's scoped dependencies (a `DbContext`, a context factory) become an accidental
  singleton.
- A task that throws is logged and retried at the next interval; it never stops the host, and it
  never stops its sibling task in the same iteration. A permanently broken purge must not
<!--#if (localAuth)-->
  also silence refresh-token cleanup.
<!--#else
  also silence any other cleanup.
<!--#endif-->
- The loop honours cancellation immediately rather than waiting out the current `Interval`, and
  does not treat a shutdown mid-iteration as a failure to log.
<!--#if (localAuth)-->
- `MaintenanceController`, `RefreshTokenMaintenanceController` and
  `SecurityEventMaintenanceController` are a second, authorized entry point to the same three purges,
  and a purge is safe to run while the timer's own pass overlaps it.
<!--#elseif (externalAuth)
- `MaintenanceController` is a second, authorized entry point to the same purge, and the purge is
  safe to run while the timer's own pass overlaps it.
#endif-->
- **Set outside 1 second – 1 day** and an instance that runs the jobs fails `ValidateOnStart()` and
  does not boot.

<!--#if (localAuth)-->
### `Jobs:Maintenance:PurgeExpiredRefreshTokensEnabled`

| Key | Type | Default | Notes |
|---|---|---|---|
| `PurgeExpiredRefreshTokensEnabled` | bool | `true` | Runs `IPurgeExpiredRefreshTokensUseCase` each iteration when `true`. |

A key of the `Jobs:Maintenance` section above, read by
`AppTemplate.Api/Features/Auth/Jobs/AuthPurgeOptions.cs` — a class of the authentication feature's
own that its composition binds to that section, so a project without authentication loses the key
with the purge. Off, rotated and expired grants accumulate: nothing else removes them, and the
maintenance job says so at warning level on every skipped iteration.

<!--#endif-->
<!--#if (anyFeature)-->
### `Jobs:Maintenance:SweepDeletedAccountsDataEnabled`

| Key | Type | Default | Notes |
|---|---|---|---|
| `SweepDeletedAccountsDataEnabled` | bool | `true` | Runs `ISweepDeletedAccountDataUseCase` each iteration when `true`. |

A key of the `Jobs:Maintenance` section above, read by
`AppTemplate.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs`. The sweep is the
guarantee behind the account-deleted fact: it applies each feature's declared policy to the owners
whose data still names a deleted account, whether or not the fact reached the feature's consumer.
Off, rows owned by an id that names no account stay where they are — the deletion event is the fast
path, and this switch is what makes it certain. See
[`ARCHITECTURE.md`](ARCHITECTURE.md#when-an-account-goes).

<!--#endif-->
<!--#if (localAuth)-->
### `Jobs:Maintenance:PurgeExpiredSecurityEventsEnabled`

| Key | Type | Default | Notes |
|---|---|---|---|
| `PurgeExpiredSecurityEventsEnabled` | bool | `true` | Runs `IPurgeExpiredSecurityEventsUseCase` each iteration when `true`. |

Read by the same `AuthPurgeOptions` class as the refresh-token switch, for the same reason: the
security-event purge is authentication's, and what it deletes — the trail — is defined by
[`SecurityEventLog:RetentionInDays`](#securityeventlog), not by this key. Off, aged-out events
accumulate; the maintenance job says so at warning level on every skipped iteration. The purge is
also an authorized endpoint, `DELETE /maintenance/security-events/expired`, for a scheduler outside
the process.

<!--#endif-->
<!--#if (reminders)-->
### `Jobs:Reminders`

| Key | Type | Default | Notes |
|---|---|---|---|
| `Interval` | timespan | `00:01:00` | How often the reminder job looks for due reminders. Range **1 second – 1 hour**. |
| `Enabled` | bool | `true` | Off leaves the rest of the process running without firing reminders — a maintenance window, or a deployment still validating the `IReminderNotifier` it wired in before letting it ring anyone for real. |

Read by `AppTemplate.Api/Features/Reminders/Jobs/JobsRemindersOptions.cs`, when
`Jobs:Enabled` is true. There is no batch-size knob: `IFireDueRemindersUseCase` takes no
command, so how many reminders one pass claims is `FireDueRemindersUseCase.BatchSize`'s concern and
not something the job could pass through. Two instances with the job enabled do not systematically
send twice — the use case takes the leader lease — but the lease is not a fencing token, so delivery
stays at-least-once.

- **Set outside 1 second – 1 hour** and an instance that runs the jobs fails `ValidateOnStart()` and
  does not boot.
- `docker-compose.yml` sets these keys explicitly for local development.

<!--#endif-->
### Not configurable — and why that is worth knowing

| Behaviour | Value | Where |
|---|---|---|
<!--#if (localAuth)-->
| Refresh token size / hash | 32 bytes CSPRNG / SHA-256 | `SessionTokensService` |
<!--#endif-->
<!--#if (todoLists)-->
| Aggregate item cap | 500 items per list | `TodoList.MaxItems` |
| Tag cap | 20 tags per item | `TodoItem.MaxTags` |
<!--#endif-->
| Readiness checks | a DbContext check and a shutdown-state check, both tagged `ready` | `Program.cs`, `Common/Hosting/ShutdownHealthCheck.cs` |
<!--#if (files)-->
| Largest single file | 5 GiB | `FileSize.MaxBytes` |
<!--#endif-->
<!--#if (todoLists)-->
| Max page size | 100 | `TodoListCollectionPolicy.MaxPageSize` |
| Default page size | 20 | `TodoListCollectionPolicy.DefaultPageSize` |
| Max sort terms | 3 | `TodoListCollectionPolicy.MaxSortTerms` |
| Sortable fields | `name`, `createdAt`, `lastModifiedAt` | `TodoListCollectionPolicy.SortableFields` |
<!--#endif-->
| Max `search` length | 100 characters | `SearchTerm.MaxLength` |
| Max cursor length | 512 characters | `Cursor.MaxEncodedLength` |
| `Cache-Control` on reads | `private, no-cache` | `Src/Presentation/AppTemplate.Api/Common/Caching/CacheHeaderExtensions.cs` |
<!--#if (tagging)-->
| Used-tag list lifetime | 1 minute | `UsedTagsCache.Lifetime` |
<!--#endif-->

<!--#if (tagging)-->
**The server-side cache has no configuration section, and that is a property of what it holds.**
One read goes through it — the tags an owner has already used, for a picker — and its lifetime is a
decision about that answer rather than about a deployment: short, because the list is also what a
client sees immediately after tagging something in another tab. `ICacheStore` takes the lifetime as
an argument, so a second cached read states its own;
<!--#else-->
**The server-side cache has no configuration section.**
No read goes through it yet, and the lifetime of one that does is a decision about that answer
rather than about a deployment. `ICacheStore` takes the lifetime as an argument, so each cached
read states its own;
<!--#endif-->
there is no global default to configure and no
key naming one. Where the cache lives is a deployment's choice and it is made in code, not here:
`AddCacheStore()` in `AppTemplate.Infrastructure.Core` registers `HybridCache` in process, and a
deployment that wants a shared second level registers an `IDistributedCache` beside that call
without any caller changing. Output caching is deliberately absent — see
`docs/ARCHITECTURE.md`'s `What is deliberately absent`.

`Cache-Control` has no setting because there is only one defensible value for a per-user
authenticated response: caching here is revalidation, not storage. An endpoint whose response is identical for every
caller may set its own header; the middleware never overwrites one already present.

<!--#if (files)-->
**The four file bounds are the anti-abuse rule of a feature that hands out signed write
URLs**, and they measure three different costs: how many unexpired write rights one caller
may be holding, how large that caller's listing may get, and how much of the bucket one
owner may fill. A caller that meets any of them gets `409` `storedFile.quotaExceeded`. The
pending bound is self-clearing — `Jobs:Files:PurgeAbandonedRegistrations*` removes
registrations that were never deposited against — so a client that crashed mid-upload waits
rather than being locked out.

<!--#endif-->
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
<!--#if (localAuth)-->
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Staging`, `Production`. Gates `appsettings.Development.json`, OpenAPI/Scalar, startup migrations and identity seeding. |
<!--#else-->
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Staging`, `Production`. Gates `appsettings.Development.json`, OpenAPI/Scalar and startup migrations. |
<!--#endif-->
| `ASPNETCORE_HTTP_PORTS` | `8080` in the container. Do **not** also set `ASPNETCORE_URLS`; it takes precedence and would silently ignore this. |

## Environment-variable mapping

`docker-compose.yml` maps every section from `.env`. The pattern:

| Configuration key | Environment variable |
|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` |
<!--#if (localAuth)-->
| `Jwt:Key` | `Jwt__Key` |
| `Identity:PasswordRequiredLength` | `Identity__PasswordRequiredLength` |
<!--#endif-->
<!--#if (externalAuth)-->
| `Authentication:External:Authority` | `Authentication__External__Authority` |
<!--#endif-->
| `Email:AllowInsecureTransport` | `Email__AllowInsecureTransport` |
| `Cors:AllowedOrigins[0]` | `Cors__AllowedOrigins__0` |
| `Database:MaxPoolSize` | `Database__MaxPoolSize` |
| `IdempotencyPurge:BatchSize` | `IdempotencyPurge__BatchSize` |
<!--#if (files)-->
| `Files:Storage:S3:BucketName` | `Files__Storage__S3__BucketName` |
<!--#endif-->
| `Jobs:Enabled` | `Jobs__Enabled` |
| `Jobs:Maintenance:Interval` | `Jobs__Maintenance__Interval` |

Colons do not work as separators in environment variables on all platforms; the double
underscore always does.

`docker-compose.yml` deliberately has **no `env_file:`** on any service. Each variable
is mapped explicitly under `environment:`, so a container holds only the keys it needs;
`env_file: .env` would additionally push `POSTGRES_PASSWORD` and every other
raw variable into the API process, readable from `/proc/self/environ` and from any
crash dump, for no benefit.

`${VAR:?message}` in the compose file makes Compose fail loudly with that message
rather than substituting an empty value. `docker compose config` is the cheap check
that `.env.example` still satisfies every one of those guards; CI runs it.
