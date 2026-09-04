# Changelog

All notable changes to this template are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**What counts as notable in a template**, since it is cloned rather than referenced: anything that
changes the *generated* project — a layer's shape, a convention the architecture tests enforce, a
configuration key, a default that affects security or deployment. A refactor invisible to someone
starting a new project from this repository does not belong here.

**What "breaking" means here**: `Src/` is a starting point, not an API, so it cannot break a caller.
A major version signals that adopting the change in an existing project derived from this template
requires manual work — a renamed configuration section, a moved namespace, a new mandatory setting.

## [Unreleased]

Nothing is released yet. The first tag will publish `1.0.0`, and
`.github/workflows/release.yml` builds the image and the migration bundle from it.

### Added

- **Mail is written in the reader's language.** Every mail ships one template per language — English
  and French — and the subject is that template's `<title>`, so a subject and a body can no longer
  end up in different languages. `AppTemplate.Api` takes the language from `Accept-Language`;
  `AppTemplate.Worker`, which has no request to read one from, uses `Localization:DefaultCulture`.
  A language with no template falls back to English, and `fr-CA` reaches the `fr` template first.
  Adding a language is adding `<Mail>EmailTemplate.<tag>.html` to two folders and nothing else:
  there is no list of supported languages, because a list can name a language no template backs.
  Two things a reader should know before touching this. The repository builds with
  `InvariantGlobalization=true` — the API's runtime image ships no ICU — so there is no `CultureInfo`
  to carry a language in and `CurrentLanguage` carries a BCP-47 tag instead. And an
  `EmbeddedResource` named `*.fr.html` needs `WithCulture="false"`, or MSBuild reads the second-to-
  last extension as a culture and compiles the template into a satellite assembly, leaving the build
  green and every mail throwing at the first send.
- **`Localization:DefaultCulture`**, bound by both hosts from twin options classes, so one deployment
  cannot answer in two languages.
- **`FileWorker:InspectDepositedFilesInterval` and `…Enabled` are reachable from `.env`**, along with
  the six `FILE_WORKER_*` and `REMINDER_WORKER_*` variables `docker-compose.yml` already read and
  `.env.example` never mentioned. The inspection interval is the one a user waits on, and it was the
  only `FileWorker` key an operator could not turn.
- **Five rules over the mail templates** (`EmailTemplateCoverageTests`): the two modules ship the
  same languages, every mail ships the fallback one, every template carries a non-empty subject, and
  no two languages of one mail share a subject — which is what a file copied and translated in the
  body only looks like.
- **A unit test for `AuditingSaveChangesInterceptor`**, which had none: it was covered only through
  the API, where the current user always resolves.
- **`CollectionOrderTests`**, which did not exist, for the one class that sees the paging mode and
  the sort order together.

- **`Storage:PublicEndpoint`, because a presigned URL covers the host it was signed for.** Measured:
  sign for `127.0.0.1:19000`, follow the URL as `localhost:19000` — same machine, same port, same
  server — and the store answers `403 SignatureDoesNotMatch`. Nothing downstream can rewrite the
  host, so the name has to be right at signing time. As shipped, `docker-compose.yml` was minting
  URLs only a container could follow, because the API reaches MinIO at `minio:9000` and a browser
  does not. Defaults to `Endpoint`, so a deployment that never knew the key is unchanged.
  It also moves two validation rules onto the endpoint they were always about: the scheme of the
  signed URL, and the refusal to hand out a bearer right in clear. The typical deployment is the
  inverse of what the code assumed — plain inside the mesh, TLS at the ingress — and was being made
  to allow insecure transport for the internal hop and then sign `http://` URLs for an `https://`
  public.
- **Deposited files are inspected before they are readable, and refused ones are quarantined.** The
  declared media type is now checked against the leading bytes and the content is scanned, which
  closes the two holes `SECURITY.md` had named — SVG declared as an image is refused outright, since
  there is no safe version of serving one inline. Inspection runs in the worker rather than in the
  request, because scanning 200 MiB inside an HTTP call is the same CPU-denial problem as resizing
  an image there: the visible consequence is a `deposited` state a client can observe, and
  `FileWorker:InspectDepositedFilesInterval` is therefore a user-facing latency rather than a
  background cost. **A scanner that cannot be reached never releases a file** — fail-open would make
  an outage the way through. `Quarantined` is persisted and terminal and costs no query a predicate,
  because the one place that hands out bytes guards with an allow-list, which refuses a new state
  the day it is added rather than the day someone extends a predicate. The refusal deliberately does
  not say what tripped it; that goes to the operator's log, not to the depositor. ClamAV speaks its
  own TCP protocol, so it escapes the outbound HTTP policy exactly as the S3 SDK does, and says so.
- **A load smoke test, run non-blocking in CI.** Every timeout, pool size and rate limit in this
  template was chosen by reasoning and none had ever been observed under concurrency. Its thresholds
  describe a broken system rather than a slow one, and a `429` counts as a pass — punishing the rate
  limiter for working would push someone to loosen it to get a green.
- **A third `IEmailSender`, over an HTTP API rather than SMTP.** `Email:Transport` picks between
  them and defaults to `Smtp`, so no existing deployment changes behaviour; an unrecognised value
  stops the process rather than falling back. It is the first real consumer of the outbound HTTP
  policy — a typed client that inherited the whole budget without asking — and it is what a
  deployment usually needs, since outbound SMTP is blocked at most hosting providers. The SMTP
  settings stop being required when the transport is HTTP, which is the point. Adds `Common/Http/`
  to the email module's folder vocabulary and a `Postmark` section whose token is a secret.
- **The rate limiter counts behind a seam, still in-process, and still without Redis.** The
  effective limit remains the configured one times the replica count — that has not changed and is
  documented — but the counting is now one type behind an abstraction, so a deployment that needs a
  shared count writes an adapter instead of rewriting middleware. Not one existing rate-limiting
  test was touched, which is the evidence that the behaviour did not move.
- **A consumer for `StoredFileDeletedDomainEvent`, which three files already claimed existed.** It
  reclaims a deleted file's bytes promptly; the sweep is what makes reclamation correct. The claim
  had been written in the persistence tracker, the worker options and the reclamation use case
  before anything implemented it, and every test was green — so `DomainEventTests` now requires
  every domain event to be either consumed or listed, with the reason, as a decision.
- **Signing in through an external identity provider, without a cookie or a redirect in sight.** The
  client runs OAuth/PKCE itself and posts the provider's `id_token`; the API verifies it — signature
  against cached JWKS, `iss`, `aud`, `exp`, `nbf` — and mints its own access/refresh pair. The token
  model is untouched, so the same endpoint serves a SPA, a mobile app and a desktop client, and
  ASP.NET's cookie-based `AddGoogle()` middleware would have contradicted the whole design.
  **One adapter driven by configuration, not one class per provider**: Google, Microsoft and Apple
  differ in values rather than behaviour, so adding Okta is a configuration entry. A link is keyed on
  `(provider, subject)` and never on the address, because Apple returns the address only at the first
  authorisation. Automatic linking needs both sides to have proved the address — a local account that
  never confirmed its own is refused rather than linked, which is the account-takeover vector this
  decision exists to close. And external sign-in runs the same tail as local sign-in, so two-factor is
  not bypassed by it. `AspNetUserLogins` had been mapped and unused since the beginning; it is now
  what the link is stored in. See `SECURITY.md`.
- **A third example feature, `Files` — the first whose two halves live in different stores.** A
  `StoredFile` aggregate in PostgreSQL and its bytes in an S3-compatible object store, joined only
  inside a use case. Upload is two-step by necessity, not by taste: the API registers metadata as
  `Pending` and returns a signed upload grant, the client writes to the store directly, and a
  confirmation verifies size and checksum against what the store reports before the file becomes
  `Available`. `RequestLimits:MaxRequestBodyBytes` is 65 536 and `IdempotencyFilter` SHA-256s the
  whole body of every `POST`, so routing bytes through the API was never on the table. Reading works
  the same way in reverse: the API issues a signed download grant and never serves a byte.
  **There is no soft delete**: `DELETE` removes the row, and the bytes are reclaimed by a sweep that
  deletes every object no row references — an effect that re-derives its own precondition, so it
  needs no outbox and no tombstone. The object key is *stored, never derived*, so changing the key
  scheme later moves no bytes; it carries an opaque prefix segment reserved for multi-tenancy that
  nothing uses yet, because adding one after two years of files would mean moving every object.
  Adds `AppTemplate.Infrastructure.Storage` (AWSSDK.S3), a `Storage` configuration section, and
  `minio` to `docker-compose.yml`. `FilesController` exposes the six user-facing operations, and
  `AppTemplate.Worker` gains a third loop for the two sweeps — the reclamation one being the
  feature's correctness guarantee rather than housekeeping, which `FileWorker:ReclaimOrphanedContentEnabled`
  says out loud. Content sniffing is deliberately still absent and is named in `SECURITY.md` as the
  gap most likely to be exploited.
- **One outbound HTTP budget, installed before the first outbound call exists.** 10 s per attempt,
  30 s in total, three retries with jitter on safe verbs only, plus the standard circuit breaker and
  concurrency limiter — set on `IHttpClientFactory`'s defaults in each host, so a module that
  registers a typed client inherits it without opting in and cannot forget it. The 30 s is chosen
  against `RequestTimeouts:Default`'s 5 minutes: an outbound call happens inside an inbound request,
  so the enclosing budget must be the longer one. Deliberately not configurable — see
  `docs/CONFIGURATION.md`. Two rules guard the escapes: `NoType_ConstructsItsOwnHttpClient` and
  `EveryHost_InstallsTheOutboundPolicy`. Adds `Microsoft.Extensions.Http.Resilience` (which brings
  Polly transitively) and `Common/Outbound/` to both hosts' folder vocabulary; the worker also gains
  outbound HTTP instrumentation, which its telemetry had skipped on the grounds that it made no
  outbound calls.
- **`ILeaderLease`, so the worker can run at more than one replica.** The reminder pass runs under
  a PostgreSQL session-level advisory lock (`PostgresLeaderLease`, on its own unpooled connection),
  which one host takes and the rest skip without waiting. Losing that host closes the session and
  releases the lock, which is the whole reason for choosing an advisory lock over a lease row with
  an expiry. The guard is in `FireDueRemindersUseCase` and not in `ReminderBackgroundService`,
  because "one host at a time" is a property of the operation and not of the timer that starts it.
  It is **not** a fencing token: leadership can be lost mid-run, so delivery stays at-least-once.
  Adds `Common/Leases/` to the persistence project's folder vocabulary, and one unpooled connection
  per replica running a leased loop to the `Database:MaxPoolSize` budget — see
  `docs/CONFIGURATION.md`, which also says why a transaction-pooling proxy would break it silently.
- **A second example feature, `Reminders`: a flat aggregate, no child entities.** A reminder is
  scheduled on a to-do item, fired by `AppTemplate.Worker` on a due-date query, and cancelled when
  its item is completed. It exists to be *different in shape* from `TodoLists` — what a to-do list's
  persistence needs only because it owns items is now legible from what a reminder does without, and
  that comparison is what decided which parts of the persistence layer were extracted and which were
  left duplicated.
- **Correctness that does not depend on event delivery** . Firing a
  reminder re-reads whether its item is still outstanding rather than trusting the event that should
  have cancelled it, cancellation assigns a state so a redelivery is a no-op, and the case where
  re-reading finds an already-completed item increments
  `apptemplate.reminders.missed_cancellations` — the number of domain events that went missing.
  Deleting an item needs no domain event at all: its reminders retire at their next due date.
- **Changing an email address**, with the current password required, the token issued for and mailed
  to the *new* address under its own named provider, an unchanged answer when that address is
  already taken, and the refresh-token revocation that a security-stamp rotation does not perform by
  itself.
- **Administrative account operations are covered by a lease on every claim** — see Fixed.
- **Raw Kubernetes manifests** under `deploy/kubernetes/`, with `docs/DEPLOYMENT.md`. Deployment,
  Service and Ingress for the API, a Deployment of its own for the worker, and a migration Job
  because the application refuses to migrate at startup. `preStop`, `Shutdown__Timeout` and
  `terminationGracePeriodSeconds` are one arithmetic and the manifests say which constrains which.
- **`docs/REMOVING-THE-EXAMPLE-FEATURES.md`**, a procedure that was carried out before it was
  written, and which states what stops being demonstrated once the examples are gone.

- **A collection-query contract: sorting, filtering and two paging modes.** `GET /api/v1/todo-lists`
  now takes `sort`, `search`, `createdAfter`, `createdBefore`, `paging`, `cursor` alongside
  `page`/`pageSize`. Sorting is multi-field with a direction per field over a **per-feature
  whitelist** (`ICollectionPolicy` in `Common/Collections/`, declared by
  `TodoListCollectionPolicy`); an unknown or unwhitelisted field is refused before a query is built,
  so no caller string ever reaches a LINQ expression. Every order ends in a unique `Id` tiebreaker,
  because without one two rows with equal keys can swap between pages and be served twice or never.
  `search` is a case-insensitive contains on the list name, executed in PostgreSQL via `ILIKE` with
  `%`, `_` and `\` escaped — it is deliberately **not** accent-insensitive. Every bound (page size,
  sort-term count, filter length, cursor length) is a `400` with its own stable code:
  `paging.invalid`, `sort.invalid`, `filter.invalid`, `cursor.invalid`.
- **Keyset (cursor) pagination alongside offset paging.** `paging=cursor` resumes from the last row
  served, so a concurrent insert cannot shift a caller's position and a deep page costs what the
  first page costs. It answers no `totalCount` — counting the match set is a second scan of it — and
  allows one sort term over a field declared keyset-capable (`lastModifiedAt` is not, being
  nullable). The cursor is opaque but unsigned, which is safe because it carries only values from a
  row the caller was served and the read query filters by owner regardless. `PagedResult<T>` gained
  `nextCursor`, and `page`/`totalCount`/`totalPages` are now nullable, being absent in cursor mode.
- **Composite indexes** `(OwnerId, <sortable field>, Id)` for each whitelisted sort field, so both
  the order and the keyset comparison are index-ordered. Migration `AddExampleFeatures`.
- **Idempotency keys on POST.** An action marked `[Idempotent]` — `POST /api/v1/todo-lists` and
  `POST /api/v1/todo-lists/{id}/items` — honours an `Idempotency-Key` header, so a client retrying
  through a flaky network cannot create twice. A replay returns the original status, body and
  `Location` plus `Idempotency-Replayed: true`; the same key with a different body is `409`
  `idempotency.keyReused`; a still-running duplicate is `409` `idempotency.inProgress`; a failed
  request releases its claim so a corrected retry succeeds. Keys are scoped **per user**, so two
  callers may use the same key string. New `Idempotency` configuration section and a `platform`
  schema; migration `InitialCreate`. Auth endpoints are deliberately **not** marked: replaying
  a login would store a bearer token in the database.
- **Policy-based authorisation, with a real operation behind it.**
  `DELETE /api/v1/maintenance/idempotency-keys/expired` requires the `Administrator` policy and
  prunes expired keys — the seeded `Admin` role now authorises something. The role name has one
  declaration (`IdentityRoles.Administrator`) shared by the seeder and the policy.
- **`Cache-Control: private, no-cache` on reads**, which is what makes the strong `ETag` this API
  already publishes worth having: a client may store a response but must revalidate, and
  `If-None-Match` then answers `304`.
- **A request body size limit** (`RequestLimits:MaxRequestBodyBytes`, default 64 KiB) replacing
  Kestrel's 30 MB default. Enforced both by middleware on `Content-Length` — answering `413`
  `request.tooLarge`, a code that already existed with nothing able to produce it — and by Kestrel
  as the backstop for a chunked body.
- **Per-consumer isolation for domain events.** A consumer that throws no longer prevents the other
  consumers *of the same event* from running; the failure is logged with the event and consumer
  type. This narrows the known gap below without pretending to close it.
- **Packaged as a `dotnet new` template.** `dotnet new install <path>` followed by
  `dotnet new cleanarch-webapi -n Your.Project` generates a solution under your own
  name, namespace, and Compose/Docker identifiers — see the README's "Using this as
  a `dotnet new` template" section. `.github/workflows/ci.yml`
  gained a `template` job that installs, generates under a different name, builds
  and tests the result on every push.
- `docs/ADDING-A-FEATURE.md`, a full walkthrough of the vertical `CONTRIBUTING.md`'s
  "Adding a feature" section summarises — aggregate → EF model → mapper → tracker →
  store → use case (with its named interface) → controller → tests → migration —
  plus what to remove by hand if you do not want the `TodoLists` sample.
- **Conditional requests on the `TodoList` aggregate.** Every read of a list or item publishes a
  strong, opaque `ETag`; every write honours `If-Match`, refusing a stale, malformed or
  unrecognised version with `412`, and `If-Match: *` against a missing or someone-else's list also
  answers `412` rather than `404`. A repeated `GET` naming the current `ETag` in `If-None-Match`
  answers `304` with no body. New `Concurrency:IfMatch` section (`Optional` by default; `Required`
  refuses a write with no `If-Match` at all with `428`).
- `ReverseProxy` configuration section (`Enabled`, `KnownProxies`, `KnownNetworks`, `ForwardLimit`)
  driving `UseForwardedHeaders`. Off by default, and the validator **refuses to start** when it is
  enabled with an empty trust set — because ASP.NET Core treats two empty lists as "trust every
  caller", which would let a client choose its own rate-limit partition.
- `TodoItem.MaxTags`, enforced by the aggregate and surfaced as a 400 by the validator, bounding
  per-item tag growth.
- `IDomainEventSource.Restore`, so events survive a failed save and a retry publishes them.
- `Tests/Integration/AppTemplate.Infrastructure.Identity.IntegrationTests`, including a real
  two-context race against PostgreSQL proving refresh-token rotation is single-use.
- A coverage floor in `coverage.minimum`, read by CI and `tasks.ps1` from the same file. Set from
  measurement (86.53% of lines over 974 tests), not invented, and the file records the measurements
  it was derived from.
- `tasks.ps1`, `AppTemplate.Api.http`, `CONTRIBUTING.md`, `SECURITY.md`, this file, and a release workflow
  publishing to GHCR with an SBOM and a signed provenance attestation.
- Dependabot coverage for NuGet, GitHub Actions, Dockerfile **and** `docker-compose.yml` — the last
  needs its own `docker-compose` ecosystem, which `docker` does not cover.
- Test projects for the two modules that had none: `AppTemplate.Infrastructure.Email.UnitTests` (50 tests —
  the SMTP validator that stops a deployment mailing in plaintext was previously untested) and
  `AppTemplate.Infrastructure.InMemory.UnitTests` (30 tests, pinning the recorded-mail ordering every
  integration test leans on).
- CI and `tasks.ps1` discover test projects from disk instead of naming them: a hard-coded list went
  stale once and a project's tests silently did not run. The only exclusion is the architecture
  suite, for the Coverlet reason recorded in the workflow; `Identity.IntegrationTests` stays out
  of `test -NoIntegration` because its rotation race needs a real database.
- Two repository-hygiene gates, in CI and in `./tasks.ps1 hygiene`:
  `.github/scripts/check-doc-paths.py` asserts every repository path cited in the documentation
  exists, and `.github/scripts/check-workflows.py` catches a dangling `needs:`, an action pinned to
  a mutable tag, a missing `permissions:`, an undefined `$VAR` and a `run:` naming a script that is
  not on disk. Both are the kind of defect no compiler and no test can see.
- A third gate, this one an architecture rule rather than a script:
  `ConfigurationSurfaceTests` pairs every `SectionName` an options class declares against
  `docs/CONFIGURATION.md`, **in both directions**. A bound section with no table, a bound key named
  nowhere, and a documented key no class binds are each a failure. The guide opens by promising that
  every setting the application reads is listed in it, and nothing had ever checked the promise —
  it was false eleven times over when the rule was written. The converse direction is the one that
  matters most: a key documented under a name nothing binds is set by an operator, ignored by the
  binder, and leaves the default in place with no sign that it did.

### Fixed

- **`AppTemplate.Worker` could not commit anything, so two features were dead.**
  `AuditingSaveChangesInterceptor` read `ICurrentUser.UserId` unconditionally on every save, and that
  host's `BackgroundCurrentUser` throws by design — so every loop that had real work threw at the
  commit. No deposited file ever became readable, and a due reminder mailed its owner and then rolled
  back, mailing them again on every pass, for ever. The stamp now asks `IAuditActor`, a separate
  abstraction answering "whom do we record" rather than "who is calling": the API answers with the
  caller, the worker answers nobody, and both are true. `ICurrentUser` still throws in the worker,
  which is the guarantee that was worth keeping. Verified end to end: a file reaches `available` and
  its bytes round-trip, and a due reminder produces exactly one mail and is marked `fired`.
  Why no test saw it: the one architecture check that composes the worker substituted a benign
  `ICurrentUser` for the throwing one. It now stands in for both adapters as the host registers them.
- **Cursor paging over an offset-only field answered `500`.** `?paging=cursor&sort=lastModifiedAt:asc`
  on to-do lists, and `sort=availableAt` on files, cleared every check and then asked the read side to
  mint a cursor for a column it has no key for, whose only recourse is to throw. It is a `400`
  `cursor.invalid` now, refused where the multi-term rule already was — before a page is served
  rather than when the next one's cursor is minted. It only fired when a next page existed, which is
  why no test met it.
- **`dotnet run Tools/Tasks.cs compose-up` failed on a healthy stack.** `--wait` counts a container
  that exited as a failure, and `minio-bucket` exits 0 by design. It is now named as a
  `service_completed_successfully` dependency of the two services that need the bucket, which is
  both the honest ordering and what stops `--wait` counting it.
- **The worker logged two errors on every cold start**, running its first passes against a schema the
  API had not migrated yet. It now waits for the API's healthcheck — a compose ordering, not a
  runtime dependency, and commented as such.
- **`AppTemplate.Api.http` could not run its own Files section.** Request 39 sent `mediaType` where
  the contract is `declaredMediaType`, so it answered `400` and every request after it depended on
  its id. Its declared size did not match its checksum either, and its upload command omitted the
  `requiredHeaders` the store insists on. The confirmation-token step called the token a query
  parameter, two lines above the comment explaining why it is a fragment.
- **`api` and `worker` did not depend on the object store**, so `--wait` could report the stack ready
  before the bucket existed.

- **The coverage gate was scoring the identity module's 187 unit tests as zero.**
  `coverage.runsettings` named `GeneratedCodeAttribute` in `ExcludeByAttribute`, and coverlet 10.0.1
  does not exclude a source-generated class with that — it drops the whole assembly holding one from
  the report. `AppTemplate.Infrastructure.Identity` holds `SecurityEventLog`, a `LoggerMessage`
  partial, so its own coverage report contained no entry for it at all, and the 68.22% recorded for
  the module came entirely from the two integration suites. The exclusion is now an `ExcludeByFile`
  glob over `obj/`, which is where every source generator writes: measured to give every other
  assembly identical line counts, to the digit, and not to lose the module. Worth eight points of
  total coverage on tests that were already written and already passing. **The floor stays at 85.**
- **The Files loop's metrics and spans were produced and thrown away, and its own documentation said
  otherwise.** `FileInstruments` declared a `Meter` and an `ActivitySource`, and
  `WorkerObservabilityExtensions` named neither — so
  `apptemplate.worker.files.{iterations,registrations_purged,objects_reclaimed,deposits_inspected}`
  and every span of that source reached no collector, at full recording cost. Nothing caught it:
  `FileInstrumentsTests` listens with an in-process `MeterListener`, which sees a measurement whether
  or not anything exports it. This is the loop that decides whether a deposited file ever becomes
  readable.
  The reminder loop, meanwhile, had no diagnostics class at all — its own comments argued in prose
  for exactly the counter the other two loops have, and settled for a log line. It now has
  `apptemplate.worker.reminders.{iterations,notified}` and a span per pass, with `disabled` as one of
  the iteration outcomes so that a loop switched off by configuration reads differently from one that
  died.
  `ObservabilityRegistrationTests` is what stops this recurring: every `Meter` and `ActivitySource` a
  host declares must be named by that host's own `Common/Observability/` registration, `AddMeter` for
  one and `AddSource` for the other, and a host is discovered from the disk so a third one is covered
  the day it exists. Broken and watched to redden by removing the two calls again.
  **`SECURITY.md` accordingly stops advising an alert on the absence of
  `apptemplate.reminders.missed_cancellations` samples** — that counter fires only on a missed
  cancellation, so silence is its healthy state and it could never have been a heartbeat. The three
  iteration counters are, and are now exported.

- **An idempotency claim now carries a lease.** A process dying between claiming a key and completing
  it left the row in progress, and every retry got `409` until the 24-hour retention purge — an
  interrupted write was unretryable for a day, for an operation that may never have happened.
  `IdempotencyOptions:ClaimLease` bounds it, an expired claim is taken over by a conditional update
  whose zero-row result is the signal, and the filter no longer releases a claim whose write had
  already committed.
- **The two-step upload can complete against a real object store.** The upload grant signed
  `x-amz-sdk-checksum-algorithm: SHA256`, which names an algorithm and supplies nothing to check
  against: MinIO accepted the deposit, recorded no digest, and `IFileContentStore.DescribeAsync` then
  threw for want of one — so **every** file deposited through the two-step upload was unconfirmable and
  the Files feature could not work outside the in-memory double. The grant now carries
  `x-amz-checksum-sha256` with the digest the client declared at registration.
  **Breaking for a derived project:** `IFileContentStore.CreateUploadGrantAsync` takes the declared
  SHA-256 as a new parameter. It also closes a second hole for free — the store refuses content whose
  digest disagrees, so a grant authorises one body rather than any body of the right length and cannot
  be replayed to swap content after a file has been inspected and released.
- **Eleven configuration keys the application reads are now in the configuration guide**, which had
  promised since its first line that all of them were. Four sections had no table at all —
  `IdentityTokens`, `TwoFactor`, `ProblemTypes` and `ReminderWorker`, the last of which
  `deploy/kubernetes/configmap-worker.yaml` sets explicitly — plus `RefreshToken:RetentionInDays`,
  `Postmark:ApiBaseUrl`, `Postmark:MessageStream` and `OpenTelemetry:TracesSamplingRatio`. Two are
  worth naming on their own: `IdentityTokens:Lifespan` is email confirmation's token lifespan and
  defaults to **a day** where the two documented siblings are an hour, and `TwoFactor` holds the
  challenge lifetime and the recovery-code count that `SECURITY.md` describes in prose as though they
  were constants. The `Postmark` table also documented a `BaseAddress` key no options class has ever
  had, which is the failure mode the new rule's converse direction exists for.
- **`ConfirmEmailAsync` rotates the security stamp**, which is what makes the confirmation token
  single-use — the command documented it as such while the token stayed replayable until it expired.
- **The worker's container image builds again**, and the release workflow publishes it. Its restore
  layer was missing a project, which `dotnet restore` skips with a log line rather than an error, so
  the failure only appeared at publish time.
- **`dotnet new` regenerates every project guid.** Five were missing from the list, so two projects
  generated from this template shared them. Generated projects no longer carry a working document or a
  `.vs` cache.
- **A stale `<see cref="…"/>` or an unused `using` now fails the build.** Nine cross-references were
  wrong, one of them naming the wrong layer for a repository contract; forty-two unnecessary usings
  were in the tree while the formatting gate believed to catch them was green.

- **A failure the framework produced carried no `code`.** The API's contract is that a client
  branches on the `code` extension and never on prose, but only errors the application *authors* went
  through `ErrorResults` and got one. A body that is not JSON, a route segment failing its `:guid`
  constraint, a verb no action accepts and an unreadable media type all came back as a bare
  `ProblemDetails` — so the promise was broken on exactly the inputs a client is most likely to get
  wrong by accident. `AddApiProblemDetails` now fills the field in for those, and only for those: a
  `code` already set is never replaced.
- **Every error response from a controller action carried `Content-Type: application/json`
  instead of `application/problem+json`.** `ApiControllerBase`'s class-level
  `[Produces("application/json")]` is a result filter that unconditionally overwrites
  `ObjectResult.ContentTypes`, clobbering the media type `ErrorResults` sets on every failure. The
  attribute is removed; `System.Text.Json` is the only output formatter registered, so a success
  response still negotiates to JSON without it.
- **Rate limiting behind a proxy was ineffective.** The limiter partitions on
  `Connection.RemoteIpAddress`; with no forwarded-headers configuration every caller shared the
  proxy's single 10-request window, so the brute-force protection did not work in production.
- **A failed save destroyed pending domain events.** Catching `ConcurrencyConflictException`,
  reloading and re-saving — the documented recovery for a lost update — published nothing at all.
- **A committed transaction could be reported as failed.** An exception from a domain-event consumer
  escaped the interceptor and surfaced as a commit failure, inviting the caller to retry a write
  that had already been applied. Events after the throwing consumer were also dropped.
- **Refresh-token rotation was not single-use.** The liveness test lived on the `SELECT`, the
  `UPDATE` was keyed on the primary key alone, and the row carried no concurrency token, so two
  simultaneous presentations of one token both succeeded. Revocation is now a single conditional
  `ExecuteUpdate`, and zero affected rows routes to the replay path.
- **A stored row could produce an aggregate violating its own invariants.** `TodoList.Rehydrate`
  appended items without checking the cap, title uniqueness, ownership or nullity.
- **Deleting an aggregate absent from the identity map lost its domain events**, because marking it
  removed was a no-op when it was not already tracked.
- A child-only change rewrote every column of the root row, including its creation stamps.
- `Result<T>.Success` accepted `null`, producing a success carrying `null` under a non-nullable
  declaration — served to a client as a 200 with an empty body.
- Loading a list produced a root × items × tags cartesian product; the read is now a split query.
- The timing-equalisation decoy hash used a different hasher from the one used to verify, so a
  configuration change could silently reopen the account-enumeration timing oracle.
- CI collected coverage over the architecture tests, which fails: NetArchTest resolves types with
  `Type.GetType(name, throwOnError: true)` and that does not work against a Coverlet-instrumented
  assembly. Coverage now excludes that project, and every test still runs exactly once.

### Changed

- **Tests run on Microsoft.Testing.Platform, and `dotnet test` takes different switches.**
  `global.json` names the runner, each test project builds as a console application hosting its own
  runner, and one invocation runs the whole solution in parallel — no per-project loop, and the
  architecture suite no longer needs a collector-free invocation of its own. The switches are not
  interchangeable with the VSTest ones a derived project may have copied into its own scripts:
  `--logger` and `--collect` are gone, replaced by `--report-xunit-trx` and `--coverage`, and
  `--settings` is accepted and then silently ignored, so coverage settings must arrive through
  `--coverage-settings`. `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` and
  `coverlet.collector` are gone with it; `xunit.v3` 4.0.0 requires the platform, and coverlet's own
  collector does not support it on .NET 10. Adopting this in an existing project means editing every
  place a test command is written down.
- **`coverage.runsettings` is a different file for a different engine.** Coverage is collected by the
  extension the platform ships, which takes .NET regular expressions where coverlet took globs, so
  every filter in that file is rewritten. It also counts sequence points rather than lines: the same
  code and the same tests measure 6,215 lines where coverlet measured 8,704, and score four points
  higher over them. Every figure in `coverage.minimum` before the `extension` rows is on the old
  scale and not comparable.
- **The coverage floor is 88, up from 85.** A floor is worth what its margin is, and re-basing the
  measurement left 85 protecting less than it used to. 88 is set against the lower of the two
  configurations measured, so a local Debug run cannot fail a gate that CI's Release run would pass.
- **`Directory.Build.targets` is new, and carries one property.** `<OutputType>Exe</OutputType>` for
  every project with `IsTestProject`. It cannot live in `Directory.Build.props`, which is imported
  before a project's own body, where `IsTestProject` is still empty.
- **A mail's subject is no longer configured.** `EmailConfirmation:Subject`, `PasswordReset:Subject`
  and `EmailChange:Subject` are gone; the subject is the template's `<title>`. They were a second
  statement of the same fact and had already drifted from it — the registration mail was being
  delivered with an English subject over a French body.
- **The README's error-code table now covers all 49 codes**, in three tables. It documented 14, while
  telling clients to branch on `code`; `auth.forbidden`, which every refused administration call
  carries, was not among them.
- **The README described two of the four file states.** `status` reads `pending`, `deposited`,
  `available` or `quarantined`, and `confirm` answers `deposited` — the value a client is most likely
  to read, and the one the document did not mention.
- **`request.malformed` is not what a wrong-typed query value returns.** The README and two request
  records promised one vocabulary for a shape error and another for a broken rule; the response
  factory has stamped `request.validationFailed` on both since it was introduced, deliberately and
  under test. The documentation now says what the code does.
- **`GET …/items/{id}/reminders` answers `200` with an empty list for an item that is not there**,
  which is now written down as the deliberate exception it is: a reminder outlives the item it is
  about, and this is the only route that can show a cancelled one.
- **`NoResponse_CarriesALinkHeader` watched for the two spellings this repository never uses.** It
  now also watches `Headers.Link`, which is the house idiom — every response header here is written
  through the strongly-typed property — and each needle set has a test proving it detects what it
  names and leaves ordinary code alone.
- **`coverage.minimum` and `coverage.runsettings` pointed at `tasks.ps1`**, deleted when the tooling
  moved to C#, and told the reader to run it.
- **`CONTRIBUTING.md` said `compose-up` starts PostgreSQL and mailpit.** It starts the whole stack.

- **Every infrastructure module now has the same shape: `Common/<Responsibility>/` plus
  `Features/<Feature>/<Responsibility>/`.** `AppTemplate.Infrastructure.Identity` had forty files in
  ten subject folders at its root and `AppTemplate.Infrastructure.Storage` nine in three, with
  neither a `Common/` nor a `Features/`, while the other three modules had both. A folder under
  `Features/` is the plural of the nature word its files carry — a `…Service` is in `Services/` and
  `Services/` holds nothing else, as a `…Repository` has always been in `Repositories/` — so a type
  name tells you its folder and a folder tells you what is in it. Uniformity beats local logic here
  on purpose: several folders hold a single file, and reading one infrastructure module now teaches
  you how to read the next. No type was renamed and no port was touched; the two modules' public
  surface is the same set of classes at new namespaces. It also puts `BucketBudget` and
  `ScannerBudget` side by side in `Common/Budgets/` — they say the same thing about two
  dependencies the outbound HTTP policy cannot reach, and each already cited the other — and moves
  `IdentityTokenOptions` to `Common/Options/`, since it sets one lifespan for every provider
  `AddDefaultTokenProviders` registers rather than belonging to any one subject.
  **Migration for a derived project:** every namespace under
  `AppTemplate.Infrastructure.{Identity,Storage}` changed, so a `using` of one will not compile.
  Fifty-one files moved and fifty-five referenced them. There is no configuration key, database
  column or wire format involved — it is a rename, and the compiler names every site.
  `LayoutConventionTests` now also fails the build for an infrastructure module absent from its two
  vocabulary dictionaries, which is what let these two drift: the previous guard could only catch a
  project listed *without* the folder it named, never one on disk that nobody listed.
- **`AppTemplate.Worker` no longer receives the API's `Jwt:Key`.** It still needs the `Jwt` section
  — it composes the identity module, and `JwtOptionsValidator` runs at startup — but it signs and
  verifies nothing, so `docker-compose.yml` and `deploy/kubernetes/configmap-worker.yaml` now give
  it a fixed placeholder and only the API's Deployment references the real key. `Jwt:Issuer` and
  `Jwt:Audience` stay identical across both hosts.
  **Migration for a derived project:** drop the `Jwt__Key` `secretKeyRef` from your worker
  Deployment and set `Jwt__Key` to any non-blank value of at least 32 bytes in its ConfigMap. No
  configuration *key* changed, so nothing fails if you do not — you simply keep handing a signing
  key to a process that never uses it.
- **The seven migrations that accumulated while the schema was still moving are recomposed into
  exactly two: `InitialCreate` (`identity` and `platform` — the schema every derived project keeps)
  and `AddExampleFeatures` (`todo` and `reminders`, and nothing else).** Net effect only: every table,
  column and index that existed before still exists after, in the same shape. This is what makes
  `docs/REMOVING-THE-EXAMPLE-FEATURES.md`'s migration step a `rm` of one file pair instead of a
  generated `DropTable` migration — a project that deletes the example features before ever running a
  migration against a real database never has their schema to begin with.
- **One closed folder vocabulary per layer, and one public type per file**. Each
  use case owns a folder holding its command, interface, implementation and validator; each port
  owns a folder holding its interface and the messages it exchanges. Architecture tests read the
  source tree and refuse a folder outside the vocabulary, an empty folder, a file declaring two
  public types, and a use-case folder that does not name what is inside it.
- **A repository contract lives in the Domain; every other port lives in Application**. The domain's `Stores/` folder is now `Repositories/`, after the contract it has
  always held; `Store` is kept for a technical contract with no aggregate behind it.

- **BREAKING: the project prefix is `AppTemplate`, not `CA`.** Every project, namespace, `.sln`/
  `.csproj`/`.http` file name, `InternalsVisibleTo`, and `.editorconfig` path-scoped section is
  renamed — CA.Domain → `AppTemplate.Domain`, CA.Api → `AppTemplate.Api`, CA.sln →
  `AppTemplate.sln`, and so on for every module and test project. The rename exists because
  `dotnet new`'s `sourceName` replaces its token by literal substring match: a `sourceName` of `CA`
  would have rewritten the analyzer rule IDs in `.editorconfig` (`CA1000`, `CA1707`, …) into
  garbage in every generated project. `AppTemplate` cannot collide with a `CAxxxx` id now or in the
  future. **Anyone who forked this repository before this change must merge or reapply that rename
  by hand** — there is no automated migration path, because there is no way to distinguish "this
  repository's own `CA` token" from an adopter's unrelated code that happens to contain `CA` once
  the fork has diverged.
- Compose's project name and the API image tag are now `app-template`/`app-template-api` (was
  `ca-template`/`ca-api`); `POSTGRES_DB`/`POSTGRES_USER` are now the generic `appdb`/`appuser` (was
  `ca_template`/`ca_app`) — generic on purpose, so a generated project's database credentials do not
  encode its name.
- `Features/<F>/DomainEvents/` is now `Features/<F>/Consumers/` — the folder holds consumers, while
  the Domain's `Events/` holds the event types.
- Feature-level error catalogues moved to `Features/<F>/Errors/`.
- **`IAuthService` is gone.** The six-method façade port is replaced by five narrow capability ports
  (`IUserAccounts`, `IEmailConfirmationTokens`, `IAccessTokenIssuer`, `IRefreshTokenGrants`,
  `IConfirmationEmailComposer`), and the sequencing moved from the Identity adapter into the Auth use
  cases — which is what finally gives `IEmailSender` a consumer in the layer that declares it. The
  refusal reasons are now explicit enums, so collapsing them to one client-facing error is a decision
  a test can watch rather than something invisible inside `SignInManager`.
- Auth request records now live in the file of the use case that accepts them, responses in
  `Features/Auth/Dtos/`, one validator per file — the shape TodoLists already had. `Contracts/`
  is gone.
- The Identity module's folders are `Bearer/`, `Notifications/`, `Options/`, `Templates/`, `Tokens/`
  and `Users/`; `Authentication/` is gone, so composing a mail is no longer filed under
  authentication.
- The two OpenAPI endpoints are mapped `.AllowAnonymous()`. They previously answered **401** in
  Development, because the default-deny fallback policy caught them — the API-reference page has
  never been reachable.
- `ITodoListRepository.GetAsync` no longer defaults its `CancellationToken`, making a dropped token
  visible at the call site.
- `DomainException` is `sealed` and always carries a message.
- Refresh-token primary keys are UUIDv7, matching the domain and giving index locality.

### Removed

- The `ValueObject` base class and its tests. Nothing derived from it: value objects in this
  template are `record`s, which is the idiom it now demonstrates without a second one alongside.
- The `Required by EF Core` parameterless constructors on the domain entities. EF maps persistence
  models, never the domain types, so they were dead code and a persistence concern in the Domain.
- Two unit tests asserting guarantees they could not verify — each configured a substitute and then
  asserted what it had just configured. The real guarantees are pinned where the behaviour lives.

### Deliberately not added

Each of these was investigated and refused, and the record says why so the next person does not
repeat the investigation. A template's value is as much in what it refuses as in what it ships.

| Capability | In one line |
|---|---|
| A filter expression language (OData `$filter`, RSQL) | Makes query cost unbounded and the whitelist unprovable; the typed surface can be read off a type. |
| RFC 8288 `Link` headers for paging | A second statement of next-page that can disagree with the envelope. |
| An outbox for domain events | At-least-once is a contract on every consumer; the effect re-derives its own precondition instead, and the divergence is counted. |
| A security-stamp cache | Invalidating at the rotation points does not propagate between instances, so the observable promise stays "within at most the TTL" and the invalidation buys nothing. |
| Rate limiting partitioned by identity | The limiter runs before authentication, so the principal is always anonymous where the partition key is computed. Moving authentication earlier would make every request the limiter is about to reject pay for a bearer validation first. |
| A paginated user search | Every other endpoint in the authentication vertical spends its effort not revealing whether an address exists; listing them all in one call would contradict that effort. |
| A caller id on `ICurrentUser` | Nothing would populate it until machine-to-machine authentication exists, and a member that is never set makes every consuming use case imply a capability the template does not have. |
| Listing and revoking active sessions | Rotation inserts a new row per refresh, so the id a client read would be dead within the access token's lifetime and `DELETE` would fail silently against a live session. It needs a session id stable across rotation first — a column, not an endpoint. |
| `PATCH` (JSON Patch or Merge Patch) | Patching a representation lets a caller assemble a state change no aggregate operation authorises. |
| Output caching | Every response is per-user, so it either serves the wrong data or has no hit rate. |
| `Deprecation`/`Sunset` headers | One version ships, so any date emitted would be invented. |
| A queryable audit trail | A table the app can `UPDATE` through its own connection has the appearance of an audit trail without the property. |
| Feature flags through OpenFeature | Every loop already has an `Enabled` key an operator can flip without a deploy, and `IOptionsMonitor` reloads them — so the kill switch exists and a vendor-neutral abstraction over one in-process provider would be an abstraction with one implementation, which this repository calls a guessed abstraction. Add it when a second provider is real. |
| A CAPTCHA on registration | It is a real anti-abuse tool, but it puts a vendor on the critical path of authentication for a template that already has per-address rate limiting and account lockout. Named as an extension point rather than shipped. |
| SMS or push for `IReminderNotifier` | The port already has two adapters; a third channel proves nothing the second did not, and adds a vendor. |
| Redis, for the rate limiter or a cache | The seam exists so an adapter can be written; the dependency does not, because this template refused output caching and the security-stamp cache and so has no other use for one. |
| Soft delete / restore | An invisible predicate on every read of every feature, where one omission leaks deleted rows silently. |

### Known gaps

Stated rather than left to be discovered:

- **Domain-event delivery is best-effort.** Consumers of one event are now isolated from each other,
  so one throwing no longer cancels its siblings — but the throwing consumer's own side effect is
  still lost, and a process that dies between the commit and the dispatch loop loses every consumer
  for that save. Closing this needs an outbox, which is refused for the reasons in
  no outbox; add one before relying on a consumer whose absence a user would notice.
- **Idempotency retention is enforced by a purge you must schedule.** `Idempotency:Retention` only
  stamps each row's `ExpiresAt`. Until `DELETE /api/v1/maintenance/idempotency-keys/expired` runs,
  a completed key stays replayable past its retention and the table grows.
- `ConfigureJwtBearerOptions` builds `ProblemDetails` and owns the `auth.required` /
  `auth.forbidden` codes, so the wire format for auth failures has an owner in the infrastructure
  layer as well as in the API.
- `UserManager` and `SignInManager` accept no `CancellationToken`, so cancellation in
  `IUserAccounts` and `IEmailConfirmationTokens` is observed on entry only and cannot be
  propagated to the I/O.

[Unreleased]: https://github.com/OWNER/REPO/compare/main...HEAD
