# Decisions already made, and the shape they impose

These are the choices a reasonable person could have made differently. They are written here, and
where a test can hold one it does — a rule nothing verifies is re-derived and lost inside six
months. If one of them is wrong for your project, change it deliberately: change the sentence here
*and* the test that holds it, in the same commit.

[`ARCHITECTURE.md`](ARCHITECTURE.md) describes the current layer boundaries and what is deliberately
absent.

**PATCH is allowed; each feature chooses its write style.** A feature may expose a complete
representation through `PUT`, named operations, or `PATCH` — whichever fits its callers. A `PATCH`
on an aggregate is a write like any other: it goes through the aggregate's domain methods and
invariants, never a blind property copy. JSON Merge Patch (RFC 7396) is the recommended default
when a feature chooses `PATCH`, because it alone says what an absent member and an unchanged one
mean; [`API-CONVENTIONS.md`](API-CONVENTIONS.md) states the shape.

**Filtering is a closed set of typed parameters.** Not a filter expression language: a grammar the
client composes is a query planner you now own, an injection surface, and a performance cliff no
index can flatten. Adding a filter means adding a parameter and a test, which is the point.

**Pagination metadata is in the body.** `PagedResult<TItem>` says whether there is a next page; no
`Link` header duplicates it. One statement of the fact, in the place every client already parses.
`NoResponse_CarriesALinkHeader` holds it.

**No `Deprecation` or `Sunset` headers while one version ships.** They would announce a schedule
nobody has committed to. `NoResponse_AnnouncesItsOwnDeprecation` holds it; delete it the day a
second version exists.

**No soft delete.** `DELETE` removes rows. A deleted flag makes every query carry a predicate that
one forgotten `Where` turns into a data leak, and it answers a retention question the audit trail
should answer instead. `NoPersistenceRecord_CarriesADeletedFlag` holds it.

**Correctness does not depend on event delivery.** Domain events are dispatched in-process, after
commit, at most once, with no outbox — so a consumer may simply not run. That is survivable only
because no consumer is the *only* thing keeping a rule true: the effect re-derives its precondition
when it runs, so running twice is the same as running once, and never running leaves the system
consistent but stale.
<!--#if (reminders)-->
A counter makes the divergence observable
(`apptemplate.reminders.missed_cancellations`, watched per [`SECURITY.md`](../SECURITY.md)).
<!--#endif-->
**Any consumer you add
has to have that shape**, or the missing outbox stops being a cost and becomes a bug.

<!--#if (reminders)-->
**The reminder pass states its own retry guarantee.** `FireDueRemindersUseCase` runs the whole pass
under `ILeaderLease`, with no per-reminder claim on top: one held in memory is invisible to the
other host, and one written to the store before the send would add a state a crash strands.
Delivery is at-least-once — each reminder is notified and then committed on its own, so a failed
commit costs that one reminder's outcome and nothing else, and a crash between send and commit
leaves it pending for the next pass; the lease narrows the window but is not a fencing token, so a
second host may still fire what the first is firing. A conflict ends the pass: the failed write
stays staged in the scope, so every later save would meet the same conflict, and the next pass
reads the conflicting reminder afresh. A failed notification backs off — a minute doubling to an
hour — and is never abandoned, and reminders come back in the order they became eligible so one
that keeps failing cannot fill every batch. Completion is re-checked in the pass rather than
trusted from `CancelRemindersOnTodoItemCompletedConsumer`, which runs outside the transaction and
is not retried: the check is what keeps firing correct independently of that delivery. An item
absent from the completion query — removed, or its list deleted — is cancelled like a completed
one and is not counted as a missed cancellation.
<!--#endif-->

<!--#if (anyFeature)-->
**Deleting an account deletes the id's data, and each feature decides what that means for its own
rows.** The account row and the feature rows live in two contexts with no shared transaction, so
Auth never reaches across: it publishes the fact `UserAccountDeleted(UserId, OccurredAt)` —
declared in `AppTemplate.Application.Core`, where every feature can consume it without naming the
module that deleted the account — in process, after the deletion has committed, with no outbox
across the two contexts. Every feature that owns user data declares its policy (delete, or
anonymise) in a consumer of that fact which re-derives its precondition through
`IUserAccountDirectory` — the account must be gone — and is idempotent, exactly the shape the
decision above demands. A leased periodic sweep over the maintenance timer is the guarantee: it
asks each feature for the owner ids that still have data and applies the same policy, so a fact
that never reached a consumer costs an interval, not the data's fate. Every shipped feature that owns user data deletes.
<!--#if (localAuth)-->
The self-service route demands re-authentication (the password when one is set, a two-factor
code when the factor is armed, an access token under five minutes old otherwise).
<!--#endif-->
<!--#if (externalAuth)-->
The trigger is an administrative call on the mapping, and a
provider-side deletion signal is that call's to wire — the template ships no endpoint for it.
<!--#endif-->
<!--#endif-->

<!--#if (files)-->
**The refusal of an outbox holds for `Files`, the feature that looks like the counter-example.** A
thumbnail that is never generated because an event went missing is visible, and bytes that are never reclaimed accumulate. It is not
one. The reclamation is a periodic sweep that re-derives its own precondition — an object no row
references is garbage — so the deletion event is a fast path that shortens an interval and nothing
<!--#if (reminders)-->
more, in exactly the shape of `CancelRemindersOnTodoItemCompletedConsumer`. Derivative
<!--#else
more. Derivative
#endif-->
generation, when a project adds it, gets the same treatment: sweep for available files without a
derivative, and let the event only make it prompt.
<!--#endif-->
**Every event is either consumed or written into a list saying why it is not.** A consumer
described in prose before it exists passes every test, because nothing else relates the events
raised to the consumers registered; `DomainEventTests` does.

<!--#if (files)-->
**A sweep is only a guarantee if it reaches everything**, so each Files pass states its retry
guarantee. All three run under `ILeaderLease`, one host at a time. **Inspection** commits each file
on its own and defers a file that gets no verdict — one minute, doubled per attempt, at most one
hour — behind the files that have waited longer: every deposited file is offered again at most an
hour after its last attempt, none can hold the front of the queue, and an outage never releases or
refuses a file. **The abandonment purge** is idempotent: a failed pass writes nothing and leaves
rows the next pass selects again. **Orphan reclamation** walks the keys in order from a checkpoint
saved after each page and reset at the end of the namespace, so every object present when a walk
starts is visited within ⌈objects / per-pass budget⌉ + 1 passes across restarts; the `FileSystem`
store's temporary files older than a day are reclaimed by the same walk.
<!--#endif-->

<!--#if (files)-->
**A stored object's key is data, minted once and persisted, not a formula over the file's id.** A
derived key would make the scheme part of the format of every stored object, so changing it would
mean copying them all, and the id is a UUIDv7, guessable in sequence, so one key would name its
neighbours. `ObjectKey.New` mints `t0/<yyyyMM>/<128 random bits in hex>`: `t0` is a reserved
partition where a tenant identifier would go, and the UTC month slice is what keeps the orphan
sweep ordered and cheap. The slice is not a licence to visit recent months only — a file deleted
today may sit under a two-year-old prefix, so the sweep covers every slice — and mint and sweep
share one method so they cannot disagree about a month boundary and delete a live file.
`StoredFileState`'s persisted integers are a column, not an order: `Deposited` is `2` and
`Available` is `1`, so reordering the members is free and renumbering one rewrites the meaning of
every row.
<!--#endif-->

<!--#if (files)-->
**The local object store is SeaweedFS, from the image its own project publishes.** It only ever
stands in for S3 in development and CI — production talks to whatever S3 provider
`Files:Storage:S3` names — but the image still runs code on every contributor's machine, so who
built it matters. MinIO went source-only in October 2025; every MinIO image still on a registry is a
third party's rebuild. SeaweedFS was taken over Garage, the next candidate, because it met every
requirement without extra machinery: it passes the whole `Storage/` integration suite unchanged
(SigV4 presigned PUT and GET, the signed `Content-Length`, `Content-Type` and
`x-amz-checksum-sha256` headers verified at deposit and the SHA-256 returned by `HeadObject`,
`ListObjectsV2` paging, a URL refused under a host it was not signed for), it creates the bucket
idempotently from a start-up flag, it refuses every anonymous request once one identity exists,
and its image carries `wget` for the healthcheck. Garage needs a layout, a key and a bucket assigned
through its admin CLI before the first request, which is a one-shot service to maintain for no
gain. Building MinIO from its source tag was the fallback, kept for the case where both failed.
<!--#endif-->

**Every container image is named by digest, with its tag beside it: `name:tag@sha256:…`.** A tag
is a pointer the registry account can move; a digest is the bytes. The tag stays because a reader
needs to know which release it is, and because Dependabot needs it to propose the next one — it
rewrites tag and digest together, which is what stops a pin from becoming a freeze. Each digest is
the multi-arch index, never one platform's manifest, so one line serves amd64 and arm64. The rule
covers `docker-compose.yml`, the Dockerfiles' `FROM` lines and `# syntax=` frontend (written
literally: Dependabot skips a `FROM` built from an `ARG`), the Testcontainers fixtures, which must
run exactly an image the compose file runs, and the images the Docker actions pull in the workflows.
`ContainerImagePinningTests` holds the first three.
<!--#if (templateMaintenance)-->
`Tools/CheckWorkflows.cs` holds the last, alongside the commit-SHA rule it already enforces for
actions.
<!--#endif-->
The frontend, the action inputs and the fixtures are outside every Dependabot ecosystem and are
bumped by hand; the test makes the fixtures fail rather than drift when the compose file moves.

**The integration suites run without Testcontainers' resource reaper.** Ryuk, the container
Testcontainers starts beside every test process to remove that process's containers if it dies, is
declared ready once its `Started` line appears in the logs read from the container's creation time
on. A WSL 2 machine's wall clock steps back by seconds every half-minute or so, and a step landing
in that half-second stamps the line before the creation: it is never read, the start waits sixty
seconds, and the fixture that triggered it fails with every one of its tests. Measured here: one
Ryuk start in seventy-eight, one full run in eight. The fixtures already remove their own
containers in `DisposeAsync`, so `ResourceReaperSwitch` turns the reaper off in both integration
projects, and what is lost is only the clean-up after a run killed outright, done by hand as
[`TESTING.md`](TESTING.md#running-it) shows.

**Extract what two real cases prove identical, and nothing more.** A guessed abstraction is a worse
defect than an assumed duplication: the duplication is visible and local, the wrong abstraction is
neither. Measure first — `wc -l`, `diff` — and require two cases that do the same thing, not two
that resemble each other. `AggregateTracker` is what that looked like when it succeeded: seven
members that read nothing but what `IVersioned`, `IAuditable` and `AggregateRoot<Guid>` already
name. `FlushTo` stayed abstract, and the two repositories were never touched.

<!--#if (accounts)-->
**Authorisation is default-deny.** The fallback policy requires an authenticated user, so a new
endpoint is closed until someone opens it. `[AllowAnonymous]` is the visible exception and it is
whitelisted by name in `DefaultDenyTests`; adding one is a line in that test.
<!--#else
**A public API admits every caller, and refuses `[Authorize]` at start-up.** A project generated
without accounts authenticates nobody, so a controller action left carrying `[Authorize]` has no
scheme to challenge with. After the controller mapping the host walks the action descriptors and
refuses to boot, naming the actions, unless an authentication scheme has come back.
`[AllowAnonymous]` still overrides, exactly as it does at request time. No test in this repository
holds it: the check lives only in the generated public variant, which this template's own suite
never builds.
<!--#endif-->

**Four words name the four ways this template reaches storage, and each one is checked.**
`Repository` loads an aggregate, so its contract lives in `AppTemplate.Domain` beside the aggregate.
`Queries` projects rows onto a DTO without materialising one. `Store` is an application port for
storage with no aggregate behind it — `IIdempotencyStore`, which a use case depends on. `Table` is
row access to one table, declared inside the persistence project and reached only by a sibling
<!--#if (localAuth)-->
infrastructure module — `IRefreshTokenTable`, which no use case has ever heard of.
<!--#else
infrastructure module, and no use case has ever heard of one.
#endif-->
`StorageVocabularyTests` holds all four: a `Store` or a `Table` naming a domain entity has become a
`Repository`, a `Repository` declared outside the Domain is a promise it cannot keep, and a `Table`
a use case depends on has become a port and needs declaring where ports are.
The six-operation ceiling `PortConventionTests` enforces is a rule about **ports** — the façade a
use case sees. A `Table` is not one and is not counted:
it is one table's whole surface, held narrow by having exactly one consumer rather than by a count.

**There is one outbound HTTP budget, it is written once, and it is a default rather than a call.**
`AppTemplate.Presentation.Core`'s `AddOutboundHttp()` installs it on `IHttpClientFactory`'s
defaults, so a module that registers a typed client inherits 10 s per attempt, 30 s in total, three
retries with jitter, a circuit breaker and a concurrency bound without knowing any of it exists. A
default beats a shared method that each module has to remember to call — nothing can forget a
default. And one policy is what makes it a policy at all: the modules that call outwards, mail and
identity, are composed by more than one host, and a budget enforced in one host only is worse than
none, because the host that misses it is the one nobody watches. The alternative shape stays refused
for its own reason — putting `HttpClient` behind an application port is the abstraction
[`ARCHITECTURE.md`](ARCHITECTURE.md) rejects by name. Two rules guard the two escapes:
`NoType_ConstructsItsOwnHttpClient` and `EveryHost_InstallsTheOutboundPolicy`.
**Retry is an allow-list of the safe verbs** — GET, HEAD, OPTIONS, TRACE — and not the package's
own deny-list, which would retry any verb it does not name. PUT and DELETE are out despite being
idempotent by specification, because that promise belongs to the server at the other end and a
default applies to servers nobody here controls. Relax it for one client whose server you know;
never widen the default. **The 30 s total is bound to `RequestTimeouts:Default`'s 5 minutes**: an
outbound call happens inside an inbound request, so the enclosing budget has to be the longer of
the two, for the same reason `RequestTimeoutsOptions` gives about the layer below it. If either
number moves, re-read both.

**Exclusivity between hosts belongs to the operation, not to the loop that starts it.**
`ILeaderLease` is an application port, and the operations that must not run twice at once
<!--#if (reminders && files)-->
(`FireDueRemindersUseCase`, the three Files passes)
<!--#elseif (reminders)-->
(`FireDueRemindersUseCase`)
<!--#elseif (files)-->
(the three Files passes)
<!--#endif-->
take it inside their use cases — not their background services. A `BackgroundService` is a trigger; a guard placed
there protects the timer's callers and nobody else, and the
<!--#if (localAuth)-->
two purges are already exposed over HTTP by
`MaintenanceController` as the standing reminder that a second caller does turn up.
<!--#elseif (externalAuth)-->
idempotency-key purge is already exposed over HTTP by `MaintenanceController` as the standing
reminder that a second caller does turn up.
<!--#else
idempotency-key purge is a use case any future caller can reach, and a second caller does turn up.
#endif--> The adapter is
a PostgreSQL session-level advisory lock (`Common/Leases/`), chosen because losing the process
releases the lock rather than stranding a lease until a timer says otherwise. **It is not a fencing
token** — leadership can be lost mid-run — so anything run under it still has to survive a second
host starting it. `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` is
what holds the first sentence: a port this layer declares and never calls is a decision that has
left the layer, and the fix is to move the decision back, not to move the file somewhere the rule
does not look.

<!--#if (localAuth)-->
**A session is a refresh-token family, and it has an identity before it has a name.** One
sign-in starts one family of grants — that fact was already true, since a replay kills the chain —
so the family is what the owner lists and ends, and its id is the `sid` claim of every access token
issued in it. What a session records about its client is its `User-Agent` at the last use and
nothing else: an address is personal data with no owner-facing use, and a session list exists to be
recognised by its owner. A credential change made from a signed-in session — a new password, a
second factor armed or dropped — ends every *other* session and spares the one that proved the new
posture; a password reset through the mailed link, made from no session, ends them all; and a token
that names no session — issued before the `sid` claim existed — spares nothing, because an unknown
session is not one to spare. Ending a session is one conditional `UPDATE` on the family row
followed by one on its live grants, in that order: a process that dies between leaves grants that
are dead on arrival, which every rotation tests, so the revocation wins its race with a rotation in
flight without holding a lock across the two.

<!--#endif-->
<!--#if (localAuth)-->
**A passkey ceremony's state is the client's to carry, not a cookie's.** ASP.NET Core Identity's own
`SignInManager` passkey methods park the ceremony state in the two-factor-user-id cookie; this API
sets no cookie and keeps no session between the two calls of a sign-in, so the adapter hands the
state to the client as an opaque string and verifies it against what the client posts back — the
same shape the two-factor challenge token has. Carrying the challenge the authenticator must sign
is not enough on its own: WebAuthn requires a challenge to be answered once and soon, and a state
the client could keep and present again would let one captured state and credential sign in
indefinitely from an authenticator that keeps no signature counter, as synced passkeys do. So the
state is sealed by a time-limited data protector — under a purpose per ceremony kind, a
registration's naming its account too — and lives `Passkey:CeremonyLifetime` (five minutes), and
it is spent on its first presentation, before the handler verifies anything: one `INSERT` keyed on
a hash of the state into `identity.SpentPasskeyCeremonies`, so the database decides which of two
presentations, on one instance or several, wins, and a ceremony that fails still uses its state
up. A rewritten, expired, wrong-kind or spent state is refused exactly as a ceremony that does not
verify. The rows live minutes, so the options calls purge the expired ones as they go rather than a
maintenance task: the purge would otherwise have needed a seventh operation on `IPasskeyService`,
past the six a port may declare, for a table no use case knows exists.
<!--#endif-->

<!--#if (localAuth)-->
**A user-verified passkey is the whole of a sign-in.** Passkey sign-in does not stop at the armed
second factor: the ceremonies demand user verification (`required`, kept unconfigurable), so
possession of the credential and the authenticator's own user check happened together — the two
factors a password and a TOTP code provide. ASP.NET Core Identity's own `PasskeySignInAsync`
bypasses the factor for the same reason. The armed factor still governs the ways in that prove
less: a password sign-in demands its code exactly as before. `CompletePasskeySignInUseCaseTests`
holds the whole sentence.
<!--#endif-->

<!--#if (localAuth)-->
**Registering a passkey demands the proof arming a second factor does.** A stolen session that
could register its thief's passkey would hand over sign-in for ever after, so the completing call
re-authenticates — the current password when one is set, and for an account holding nothing its
holder could present (a provider-linked one) a recently issued access token, the same fallback and
window account deletion takes. Registering a passkey ends no session and rotates no stamp: a new
credential changes what future sign-ins can present, not what the sessions already open are
worth. Removing one is the other direction, and is treated as a password change is: the stamp
rotates (the adapter rotates it, since Identity's own removal does not) and every session but the
caller's ends, so a session opened with a withdrawn passkey — a lost authenticator's — does not
outlive the withdrawal. A token naming no session spares nothing, as for a password change.
<!--#endif-->

<!--#if (localAuth)-->
**The email is the account's only identifier.** A separate user name was a second identifier to
choose, validate, keep unique and display, and a second value whose collision registration had to
report. The application carries none. ASP.NET Identity requires `UserName`, so the column stays and
is kept equal to the email: set at creation, taken from the address for an account provisioned
through a provider, and moved in the same update when an email change is confirmed. Identity's
allowed-character set is cleared, since the address's own validation is the rule for it. A project
that wants a display name adds it as profile data, never as a second way to sign in.
<!--#endif-->

<!--#if (localAuth)-->
**Registration never says whether an address is taken.** Every well-formed request answers `202`
with one body, and the submitted address learns by mail what happened: a confirmation link, a fresh
one, or a notice that it already has an account. Refusals of the submitted values themselves stay
`400` and are judged before the address is looked up, so their order reveals nothing. The cost is a
client that cannot say "that address is taken" inline; the mail says it instead, to the only person
entitled to know. An email change naming a taken address follows the same rule.
`RegisterUseCaseTests` and `RegistrationFlowTests` hold it.
<!--#endif-->

**No MediatR, no dispatcher, no pipeline behaviours.** A controller names the use case it calls, and
`F12` reaches the implementation: the call graph is a call graph, and the compiler checks the wiring
instead of a runtime registry. MediatR would add a `Command` type, a `Handler` type, an `IRequest<>`
marker and a runtime dispatch step to reach exactly the same method, and the pipeline behaviours it
offers are available without it — FluentValidation runs in the use case, logging is `ILogger`, and
the transaction boundary is `IUnitOfWork`. This is not an argument against MediatR in general, but
for not paying for it before there is a pipeline to put in it: adding it later is mechanical,
removing it once every handler assumes it is not. `LayerDependencyTests` forbids the package by name.

**The HTTP pipeline is one call, `UseHttpPipeline()`, in one order.** Six of its orderings are coupled
pairwise, so a step a host could insert in the middle would offer exactly the arrangement the call
exists to prevent. Forwarded headers come first, before anything reads the client address or scheme:
the rate limiter partitions on the remote address, and CORS, authentication and the exception handler
observe the scheme. Security and cache headers come next, so a response written by the exception
handler, the rate limiter or a health endpoint carries the headers a controller's does — both write
from `OnStarting`, which survives `UseExceptionHandler`'s `Response.Clear()`. The request log sits
outside the exception handler and ahead of the size limit, so its entry reports the status the caller
received and a 413 is answered without calling the next middleware; the limits come before anything
reads the body. The language and the timeout follow the rate limiter, so a rejected request does no
work and starts no clock, and the timeout precedes authentication and authorization so its deadline
covers them. HTTPS redirection is not installed and HSTS is not sent: TLS terminates upstream, and
the HSTS attributes are commitments over a whole domain that only the component terminating TLS knows.

**A port never exposes `IQueryable`.** A contract that hands out a query tree has not abstracted the
database, it has published it — and every caller becomes a place where a lazy load can happen.
`NoApplicationPort_ExposesAQueryable` holds it.
