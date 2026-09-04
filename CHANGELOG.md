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

- **A second example feature, `Reminders`: a flat aggregate, no child entities.** A reminder is
  scheduled on a to-do item, fired by `AppTemplate.Worker` on a due-date query, and cancelled when
  its item is completed. It exists to be *different in shape* from `TodoLists` — what a to-do list's
  persistence needs only because it owns items is now legible from what a reminder does without, and
  that comparison is what decided which parts of the persistence layer were extracted and which were
  left duplicated (`docs/adr/0027`).
- **Correctness that does not depend on event delivery** (`docs/adr/0026`, amending `0017`). Firing a
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
  because `docs/adr/0009` refuses to migrate at startup. `preStop`, `Shutdown__Timeout` and
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
  the order and the keyset comparison are index-ordered. Migration `AddTodoListSortIndexes`.
- **Idempotency keys on POST.** An action marked `[Idempotent]` — `POST /api/v1/todo-lists` and
  `POST /api/v1/todo-lists/{id}/items` — honours an `Idempotency-Key` header, so a client retrying
  through a flaky network cannot create twice. A replay returns the original status, body and
  `Location` plus `Idempotency-Replayed: true`; the same key with a different body is `409`
  `idempotency.keyReused`; a still-running duplicate is `409` `idempotency.inProgress`; a failed
  request releases its claim so a corrected retry succeeds. Keys are scoped **per user**, so two
  callers may use the same key string. New `Idempotency` configuration section and a `platform`
  schema; migration `AddIdempotencyKeys`. Auth endpoints are deliberately **not** marked: replaying
  a login would store a bearer token in the database.
- **Policy-based authorisation, with a real operation behind it.**
  `DELETE /api/v1/maintenance/idempotency-keys/expired` requires the `Administrator` policy and
  prunes expired keys — the seeded `Admin` role now authorises something. The role name has one
  declaration (`IdentityRoles.Administrator`) shared by the seeder and the policy.
- **`Cache-Control: private, no-cache` on reads**, which is what makes the strong `ETag` this API
  already publishes worth having: a client may store a response but must revalidate, and
  `If-None-Match` then answers `304`. See `docs/adr/0019`.
- **A request body size limit** (`RequestLimits:MaxRequestBodyBytes`, default 64 KiB) replacing
  Kestrel's 30 MB default. Enforced both by middleware on `Content-Length` — answering `413`
  `request.tooLarge`, a code that already existed with nothing able to produce it — and by Kestrel
  as the backstop for a chunked body.
- **Per-consumer isolation for domain events.** A consumer that throws no longer prevents the other
  consumers *of the same event* from running; the failure is logged with the event and consumer
  type. This narrows the known gap below without pretending to close it — see `docs/adr/0017`.
- **Packaged as a `dotnet new` template.** `dotnet new install <path>` followed by
  `dotnet new cleanarch-webapi -n Your.Project` generates a solution under your own
  name, namespace, and Compose/Docker identifiers — see the README's "Using this as
  a `dotnet new` template" section and `docs/adr/0014`. `.github/workflows/ci.yml`
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
  refuses a write with no `If-Match` at all with `428`) — see `docs/adr/0013`.
- `ReverseProxy` configuration section (`Enabled`, `KnownProxies`, `KnownNetworks`, `ForwardLimit`)
  driving `UseForwardedHeaders`. Off by default, and the validator **refuses to start** when it is
  enabled with an empty trust set — because ASP.NET Core treats two empty lists as "trust every
  caller", which would let a client choose its own rate-limit partition.
- `TodoItem.MaxTags`, enforced by the aggregate and surfaced as a 400 by the validator, bounding
  per-item tag growth.
- `IDomainEventSource.Restore`, so events survive a failed save and a retry publishes them.
- `Tests/Infrastructure/AppTemplate.Infrastructure.Identity.UnitTests`, including a real two-context race
  against PostgreSQL proving refresh-token rotation is single-use.
- A coverage floor in `coverage.minimum`, read by CI and `tasks.ps1` from the same file. Set from
  measurement (86.53% of lines over 974 tests), not invented, and the file records the measurements
  it was derived from.
- The ADR index is now checked against the records on disk: a new record with no row in
  `docs/adr/README.md`, a row linking a record that was deleted, or two records sharing a number all
  fail `./tasks.ps1 hygiene`. An unindexed record is invisible to anyone starting from the index, and
  nothing else notices.
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
  suite, for the Coverlet reason recorded in the workflow; `Identity.UnitTests` stays out of
  `test -NoIntegration` because its rotation race needs a real database.
- Two repository-hygiene gates, in CI and in `./tasks.ps1 hygiene`:
  `.github/scripts/check-doc-paths.py` asserts every repository path cited in the documentation
  exists, and `.github/scripts/check-workflows.py` catches a dangling `needs:`, an action pinned to
  a mutable tag, a missing `permissions:`, an undefined `$VAR` and a `run:` naming a script that is
  not on disk. Both are the kind of defect no compiler and no test can see.

### Fixed

- **An idempotency claim now carries a lease.** A process dying between claiming a key and completing
  it left the row in progress, and every retry got `409` until the 24-hour retention purge — an
  interrupted write was unretryable for a day, for an operation that may never have happened.
  `IdempotencyOptions:ClaimLease` bounds it, an expired claim is taken over by a conditional update
  whose zero-row result is the signal, and the filter no longer releases a claim whose write had
  already committed.
- **`ConfirmEmailAsync` rotates the security stamp**, which is what makes the confirmation token
  single-use — the command documented it as such while the token stayed replayable until it expired.
- **The worker's container image builds again**, and the release workflow publishes it. Its restore
  layer was missing a project, which `dotnet restore` skips with a log line rather than an error, so
  the failure only appeared at publish time.
- **`dotnet new` regenerates every project guid.** Five were missing from the list, so two projects
  generated from this template shared them. Generated projects no longer carry `HANDOFF.md` or a
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

- **One closed folder vocabulary per layer, and one public type per file** (`docs/adr/0025`). Each
  use case owns a folder holding its command, interface, implementation and validator; each port
  owns a folder holding its interface and the messages it exchanges. Architecture tests read the
  source tree and refuse a folder outside the vocabulary, an empty folder, a file declaring two
  public types, and a use-case folder that does not name what is inside it.
- **A repository contract lives in the Domain; every other port lives in Application**
  (`docs/adr/0024`). The domain's `Stores/` folder is now `Repositories/`, after the contract it has
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
  the fork has diverged. See `docs/adr/0014` for the packaging decision this rename made possible.
- Compose's project name and the API image tag are now `app-template`/`app-template-api` (was
  `ca-template`/`ca-api`); `POSTGRES_DB`/`POSTGRES_USER` are now the generic `appdb`/`appuser` (was
  `ca_template`/`ca_app`) — generic on purpose, so a generated project's database credentials do not
  encode its name. See `docs/adr/0014`.
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

| Capability | Record | In one line |
|---|---|---|
| A filter expression language (OData `$filter`, RSQL) | [0015](docs/adr/0015-typed-filters-not-a-filter-expression-language.md) | Makes query cost unbounded and the whitelist unprovable; the typed surface can be read off a type. |
| RFC 8288 `Link` headers for paging | [0016](docs/adr/0016-pagination-metadata-in-the-body.md) | A second statement of next-page that can disagree with the envelope. |
| An outbox for domain events | [0017](docs/adr/0017-no-outbox-for-domain-events.md) | At-least-once is a contract on every consumer and needs a dispatcher, dead-letter path and lag monitoring a template cannot default. |
| `PATCH` (JSON Patch or Merge Patch) | [0018](docs/adr/0018-no-patch.md) | Patching a representation lets a caller assemble a state change no aggregate operation authorises. |
| Output caching | [0019](docs/adr/0019-caching-is-revalidation-not-storage.md) | Every response is per-user, so it either serves the wrong data or has no hit rate. |
| `Deprecation`/`Sunset` headers | [0020](docs/adr/0020-no-deprecation-or-sunset-headers.md) | One version ships, so any date emitted would be invented. |
| A queryable audit trail | [0021](docs/adr/0021-no-queryable-audit-trail.md) | A table the app can `UPDATE` through its own connection has the appearance of an audit trail without the property. |
| Soft delete / restore | [0022](docs/adr/0022-no-soft-delete.md) | An invisible predicate on every read of every feature, where one omission leaks deleted rows silently. |

### Known gaps

Stated rather than left to be discovered:

- **Domain-event delivery is best-effort.** Consumers of one event are now isolated from each other,
  so one throwing no longer cancels its siblings — but the throwing consumer's own side effect is
  still lost, and a process that dies between the commit and the dispatch loop loses every consumer
  for that save. Closing this needs an outbox, which is refused for the reasons in
  `docs/adr/0017`; add one before relying on a consumer whose absence a user would notice.
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
