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
flight without holding a lock across the two. Ending every session of an account is the same two
statements, never a loop over the grants: it runs on the replay path, which an attacker can trigger
at will, and a round trip per grant is the amplification that path must not have. It also takes the
live grants of sessions already ended, since a rotation in flight can leave one behind.

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
past the six a port may declare, for a table no use case knows exists. A record outlives its state
by five minutes: the state's expiry is read against the clock of whichever instance it reaches, and
a record purged the instant it expired on one instance would let another, whose clock runs behind,
accept the state once more.
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
window account deletion takes. The proof is judged before the ceremony reaches the adapter, so a
refused proof leaves the state unspent and a retry with the right evidence needs no new options
call. Registering a passkey ends no session and rotates no stamp: a new
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

## Domain and application

**A non-owner is answered exactly as an absent aggregate.** An endpoint that takes an id and tells
"not yours" from "not there" becomes a way to ask whether an id belongs to somebody. `OwnedAggregate`
therefore answers both with the same error, and which error says "absent" is the feature's to
choose, so it is passed in rather than minted in the core. Ownership goes into the query, not into a
check after it: fetching first would already have pulled another user's data into memory. The
precondition on a write is compared against the aggregate the use case just loaded, because only the
use case holds it, so nothing can commit between the comparison and the write; the check runs after
the existence check, so a stale caller sees `412` before `404`, which RFC 9110 allows and which
confirms nothing about an item to a caller working from an outdated list. An empty precondition set
is one nothing can satisfy, the right answer for a validator this application never issued.

**The version token travels beside the value, and comes from the query that read the value.** A
caller that publishes a validator has it, one that only needs the representation ignores it, and the
DTO stays a description of the resource with no field a client could mistake for domain data. Reading
the version in a second query would leave a window in which it and the body disagree, and a validator
that does not describe the body it was sent with is worse than none.
<!--#if (todoLists)-->
The one place a write answers without a read-back is the to-do list mapping, and it is sound only
because the tracker writes the store-assigned version and audit timestamps onto the aggregate inside
`SaveChangesAsync`, and because nothing between the write and the projection mutates the aggregate
again: a consumer running inside the save must not reach back into the aggregate it was raised
from. The items in that projection are ordered exactly as the detail query orders them, so a list reads
the same whether it comes back from a write or from a query.
<!--#endif-->

**Collection requests are bound in two steps, and the cursor is resolved last.** Sort and paging mode
are parsed first, the feature builds its own filter second, and the cursor is decoded last against the
order it was minted under, so each refusal comes from the party that owns the rule: the core refuses
what is wrong with the order, the feature what is wrong with its filter. A cursor sent under another
sort is refused, not ignored or re-minted: the read side compares the cursor's key through the
request's sort term, so resuming under a different sort would parse a name as a date and throw, ignoring
the sort would serve an order nobody asked for, and re-minting would skip or repeat rows. A combination
the template's keyset does not implement (more than one key plus the tiebreaker, or an offset-only field
under a cursor) is refused whether or not a cursor was sent, so a caller is not let through on page 1
and refused on page 2. A cursor sent with offset paging fails rather than being silently ignored.
`FeaturePageRequest` can only be made by the binder, after every one of these checks has answered, so
the read side never re-validates what already cleared the whitelist and there is no second way in.

**A cursor is opaque, not authenticated.** It carries only values the caller was already served on a row
they were allowed to see, and the resuming query still filters by owner whatever the cursor claims, so
tampering can only change which of the caller's own rows come next. What a signature would otherwise
catch is a key edited into a value the field cannot parse, so the feature names its date fields and
the check runs before the request reaches the port: a tampered cursor lands where every malformed
request does, a `400` with `cursor.invalid`, not a `500` out of the keyset predicate.

**A sort field is a declaration, and declarations are constructible.** The whitelist is safe because
the policy handed to `SortOrder.Parse` is fixed at compile time, so building a `SortableField` grants
nothing to anybody; `SortTerm` is the one type that carries caller input, and it is made only by the
parser after the whitelist has answered, so its field is always the whitelist's own spelling and every
downstream switch can compare it ordinally. The factories stay public so a fitness test can declare a
policy naming a field no translator handles and prove that the translator's `default` arm throws: a guard
nothing can construct a case for is a guard nobody has checked. A blank sort parses the policy's own
default through the same code path, which is why a blank default is a programming error in the policy
and not a request to reject. Collection policies are plain classes with a parameterless constructor so
an architecture rule can discover and check every one without a container.

**The validating factories of the core are internal, and granted to exactly one test assembly.**
`Cursor.Decode`, the cursor keys and the sort term factories must not be constructible by anything
that skipped validation. A test restricted to the public surface reaches their failure branches only
through a use case, which proves a request is refused without showing which of a dozen branches refused
it, so the application core grants its internals to its own mirror test project alone. The persistence
unit tests are deliberately not granted: a test that could build an unvalidated sort or cursor would
assert on SQL no request can produce, and uses the public `Cursor.After` and `SortOrder.Parse` instead.
The application project does the same for its mappers, its media-type signature table and its password
policy, each a decision one feature owns and reaches only through the use case above it.

**The cache is for answers whose staleness is harmless.** `ICacheStore` holds nothing that decides an
authorisation, enforces a bound, becomes an `ETag` or tells something else what to delete: a cached bound
is a bound multiplied by the number of processes holding a copy, and a cached version is a precondition
that no longer describes what it guards. An entry may vanish before its lifetime ends, so a caller is
correct when it does; the port promises to be faster, never to remember.
<!--#if (tagging)-->
The used-tags list qualifies, being a suggestion in a picker or a filter, and a list a minute out of date
costs a tag typed in full. The entry is dropped by whatever changes an owner's tags, so the lifetime is
only the ceiling on how long a change made elsewhere stays unseen.
<!--#endif-->

**A lease takes work as a delegate, and an exception is never read as "somebody else has it".** The
exclusion lasts exactly as long as the work, and an exception from it, cancellation included, reaches
the caller with the lease already released instead of coming back as `false`: a caller that could not
tell "it failed" from "standby" would log a failed pass as a quiet one. The fencing limit is stated
under the lease decision above.

**A domain event is published by the operation that made it true, after the write commits.** A fact no
aggregate carries is published by the use case or service, never before the commit, which would hand a
consumer a fact the store can still deny. Publication is in-process with no retry, so a consumer that
throws is logged and skipped; the guarantee behind a fact that must not be lost is the sweep, as above.
Consumers live in the application layer on purpose: deciding what happens next is application
behaviour, and a consumer then needs nothing from any infrastructure module. The abstraction is named
consumer, not handler, because CA1711 reserves the `EventHandler` suffix for delegates and a reader who
sees it on a public type is entitled to expect the .NET event pattern. The typed bridge cannot be
inherited twice, the call being ambiguous: a consumer of two event types implements the untyped
interface and switches, or is split in two. A mis-registered consumer fails with a message naming the
consumer and the event, the one thing needed to find the registration that caused it.

**A consumer of a deletion re-derives its precondition and is idempotent by shape.** Each one asks the
account directory whether the account is really gone, so an account that exists again, or never left,
keeps its data whatever the call was told, and an owner with nothing left is a finished owner, which is
also what a redelivery finds, so nothing is loaded and saved to prove it. One call is one transaction of
the feature's own, committed before it returns: the account's deletion has already committed and a
failure here is never reported as a failure of it; a failure propagates and the next attempt starts
from the precondition, never from a partial run. Shutdown ends a sweep pass quietly instead of being
logged as a failed owner.

**A use case catches only what the caller could not have avoided.** The validator is the first line, so
a `DomainException` that reaches a use case from something the validator already refused means the two
disagree, which is a bug to surface and not an outcome to map. Where the aggregate refuses on its
current contents (a duplicate title, a full list, a tag cap, a reminder no longer pending, a due date
measured against the clock at the moment of the call) the use case catches it and answers with an error
result, because no better input would have avoided it. Where the operation is idempotent by shape, or
rejects only what was already checked, there is no `try`. A null argument is a broken call and not a
caller driving the aggregate into a forbidden state, so it raises the guard exception, not the domain
one. The validators measure strings after trimming, as the domain does, so a name the domain accepts is
one the validator accepts too.

**An error compares structurally, and a result is unwrapped by failure first.** The generated equality
of `Error` would compare the details dictionary by reference, so two errors built from separate
dictionaries with equal contents would differ; equality and the hash are written by hand, and they agree.
`Result<TValue>.Value` throws on a failure, so a property pattern that reads it throws instead of failing
to match: the failure is answered before the value is read, and a result over an interface type is built
with `Result.Success` because C# applies no user-defined conversion from or to an interface.

**The current language is a tag, and it is ambient.** The repository builds with invariant globalization,
so at run time only the invariant culture exists and getting a named culture throws; a language tag lets
the template choose a language without asking a deployment to add ICU. It is ambient because the
language is a property of the exchange and not of one call, and it lives in the application layer
because two infrastructure modules read it and a module may not reference a sibling. A null tag is
refused outside the iterator that enumerates the candidates, since an iterator runs nothing until it is
enumerated and the refusal would otherwise surface several frames from the caller that passed it.

**Idempotency is keyed by user and fingerprint.** The user keeps two callers from colliding over the same
key string, and the fingerprint tells a genuine retry from a different request that reuses the key.

<!--#if (tagging)-->
**A tag set is the aggregate's, its rules are shared.** The aggregate chooses the cap and the subject, and
the set enforces them together, so the cap cannot be bypassed by reaching the collection another way. A
duplicate is a no-op, not an error, so a retried request cannot fail spuriously, and the cap is checked
only for a tag that is actually new for the same reason. A replacement is total, not a merge, and removals
happen first: twenty tags swapped for twenty others is not a request for forty, and a refused replacement
leaves the set as it was. The validation in the application layer restates the domain's rules from its
constants, so a caller gets one `400` naming every field it got wrong instead of a `500` from the first
exception; an absent set is refused and an empty one is not, because answering them alike would make
clearing a set impossible to express.
<!--#endif-->

<!--#if (files)-->
**Names, keys and media types are refused on the character, not sanitised.** A stored file name
refuses path separators and the characters Windows refuses, because without a separator no arrangement of
dots can leave a directory: an embedded `..` is allowed, and a stem that is a Windows device name is
refused with or without an extension, since the device resolves before the directory is consulted.
Trailing dots go with trailing whitespace, which Windows strips, and NUL and newline are refused because
NUL truncates the name for C APIs and a newline would inject a header line into `Content-Disposition`. An
object key is narrower than the store accepts, because it is pasted into a signed URL path where every
character a URL parser, shell or filesystem treats specially is a place for components to disagree, and it
is lower case only so case-insensitive tooling cannot see two keys as one; it is not trimmed, unlike the
other text value objects, and its parser accepts two segments though new keys have three, so a key minted
under another scheme still loads, while empty segments and dot segments are refused as traversal that
proxies and signers do not resolve alike. A declared media type is compared case-insensitively per RFC
9110, refuses parameters rather than stripping them, and treats the wildcard as its own rule, because it
states what a client accepts, not what a file is. A checksum is normalised and not required to arrive in
one case, as the client and the store compute the two digests. Loading goes through the same factory as
minting, so the two cannot disagree about what is valid, and the aggregate's shared invariants live in one
place so registering and rehydrating add only their own checks.

**The content policy decides what the head of a file shows, and no more.** An outage of the inspector is
never read as a pass, so it is checked before anything else, and a file too large for the scanner is
permanently not inspectable: retrying would park it for ever and releasing it would make an oversized upload
the way to skip the scan. Script containers are refused first whatever the declared type, because
nothing sanitises an SVG; the markup is looked for in the prefix because it has no byte signature, and in
two places because markup can be pushed past the prefix but not away from the start. A byte-order mark is
stripped from the bytes, not from a decoded string, as an ASCII decode would turn a UTF-8 mark into
placeholders the scan stops on and every marked document would pass as not markup; UTF-16 is matched
with padding. When the content names itself the match is exact, so `image/jpg` for a JPEG is refused and
not guessed at. The signature table holds only signatures that identify exactly one type: the ZIP
header also opens every Office, OpenDocument, EPUB and JAR file, so reading it as ZIP would falsely
refuse an honest `.docx`, while a container declared as an image is still refused for lacking an image
signature. A declared type with no signature (CSV, JSON, text, ZIP-based documents) is released: the limit of
what a head decides. A stream is read until the head is full, because one read of a network stream returns
whatever has arrived, and the inspector records the reason a file was refused while keeping the malware
signature away from the uploader.

**A file's states are related by an equivalence over the whole enum.** The aggregate states the
correspondence between its states and its stored form once, over every value rather than a rule per
state, so a new state cannot be exempt. A tag replacement goes through the tag set, so too many tags or a
duplicate refuse the row, and the use case catches that refusal as the second line behind the validator. A
zero-byte object, which an interrupted deposit leaves, is refused by the size value object inside the same
guard, and a deposit is offered again at least hourly. The id travels in URLs, so answering a non-owner as
absent matters here in particular. A download answers "not available" as the only outcome worth retrying,
and a store fault the caller cannot act on is reported and answered the same way. Reclaiming the bytes of a
deleted file swallows a store failure, because the deletion has committed and the sweep reclaims the
bytes: propagating would report a store outage as a failed deletion.
<!--#endif-->

<!--#if (reminders)-->
**The reminder pass never claims more than the lease gives.** The lock key is derived from the use case's name
string, so during a rolling deploy every host must spell it identically: renaming it would give two releases
different keys, each uncontended, and both would fire the same batch believing itself the leader. The
lease runs work that returns nothing, so the count of fired reminders comes back through a local the
delegate closes over, still zero on the standby path because the delegate never ran, which is why a
standby pass is logged apart from a pass with nothing due. The completion of each item is read in one
query for the whole batch. A reminder is built before its due date is checked, so the constructor's
invariants are what an ill-formed call meets, and being due in the future is a precondition of scheduling
that the load path must not re-check. Rescheduling gives the reminder a new moment, and the failures
counted so far were against a moment that no longer applies. A cancelled run is not a failed notification.

**Listing the reminders of an item does not check that the item exists.** The route answers `200` with an
empty list where every other route into an item answers `404`: removing an item cancels its reminders and
does not delete them, so a `404` would make a cancelled reminder unreachable. Nothing leaks either way,
since the owner filter shows a stranger an empty list whether the item is theirs, somebody else's or
nobody's.
<!--#endif-->

<!--#if (todoLists)-->
**A to-do item cannot be completed at the default instant.** The default `DateTimeOffset` is what an
uninitialised caller passes, and accepting it would complete the item at year 1, which no reader could tell
from a real value. An item is read through its list's projection, with no port of its own, so the query that
finds an item is the one that enforces ownership.
<!--#endif-->

<!--#if (notes)-->
**A note is a feature without a domain model.** A note without rules to protect needs no aggregate, so
its service is the whole application layer of it: reads stage and commit nothing, which is the only
separation CQS asks for, and every failure for a note the caller cannot see is the same not-found,
whether the note is missing or somebody else's. The version is assigned by the database, so a write's
answer is read back and not assembled. An empty body is a note with a title only; a missing one is a
malformed request.
<!--#endif-->

<!--#if (externalAuth)-->
**External identity mappings are written in transactions of their own.** Each write is independent of
the request's unit of work. Two first requests from the same caller race on the unique index over issuer and
subject: the loser's insert is refused and it reads the winner's row, so both answer the same owner, as
does a retry of an insert that had in fact committed. A transient failure is retried by the execution
strategy by replaying the whole transaction, and anything else propagates, because there is no fallback
owner. Removing a mapping commits before it returns and a second call answers that the mapping is not found;
a caller that signs in again is provisioned afresh, under a new owner id with none of the old one's data.
The facts about a mapping are published after the store has committed, in the same request, and the
features holding data under that id hear them from their own consumers.

<!--#if (localAuth)-->
## Authentication

**Every security-stamp rotation owes the refresh tokens a revocation, and one policy pays it.** The
stamp fails every access token at its next validation, but refresh tokens are this module's own and
survive it. ASP.NET Identity also leaves the stamp alone on several changes that take access away
or alter it — a lockout, a role granted or revoked, a passkey removed, an email confirmed — so the
adapters rotate it themselves. `CredentialInvalidationPolicy` is where a use case then revokes the
grants and records the rotation, after the store accepted the change and never instead of rotating.
It has two forms: a password change, arming or dropping the second factor and removing a passkey
spare the session the change was made from, which has just proved the new posture; everything else
ends every session, an email change and a reset included. A token naming no session spares nothing,
because an unknown session is not one to spare. Four
operations skip it on purpose. Confirming an email rotates the stamp but no session can exist before
confirmation, since sign-in requires it — a deployment that sets `Identity:RequireConfirmedEmail` to
`false` has to add the revocation. Provisioning a two-factor secret rotates through Identity's own
`ResetAuthenticatorKeyAsync` and arms nothing, so signing every device out would punish a step that
changed nothing. Deleting an account leaves no stamp to rotate and its grants go by cascade.
Unlocking gives access back and rotates nothing. "Sign out everywhere" is the converse: it revokes
every grant and rotates nothing, since a rotation would also kill the access token asking, which
therefore lives out its lifetime. A new operation that rotates the stamp calls the policy.

**Proving the current password never rotates the stamp and never counts towards lockout.**
Re-authentication, arming and disarming the second factor and an email-change request check the
password through the hasher's `VerifyHashedPassword`, not `UserManager.CheckPasswordAsync`, which
rewrites the hash on a rehash-needed result and so rotates the stamp — ending the very session
making the proof. A failed proof is not counted either: it must not lock out an account its holder
is signed into. Sign-in is the one path that counts, through `CheckPasswordSignInAsync` with
`lockoutOnFailure`, because lockout is what bounds online guessing.

**The password policy has one authority, the validated `Identity` section.** ASP.NET Identity
applies it to every new password, at registration, change and reset. The application layer checks
only `PasswordInputPolicy` — present, and short enough that an arbitrarily long PBKDF2 input cannot
become a denial of service — because a second copy of the policy would drift from the first. Every
member of the section has a safe default: whatever is bound overwrites Identity's own defaults, so a
member left at `false` or `0` by omission would loosen the policy instead of leaving it alone. The
required length has a floor of eight that validation enforces and binding clamps to. A password
that is checked rather than set — at sign-in, or as the current one — gets presence only, since it
may predate the current policy.

**An administrator cannot point an access-removing capability at their own account.** Locking an
account and removing a role rotate the stamp, so the request would end the session making it, and
perhaps the only one able to undo it; deleting is the same with nothing left to undo.
`SelfAdministrationPolicy` refuses all three before the store is touched, and the role guard covers
every role, not the administrator one: the application layer never spells that name — `IdentityRoles`
does, once, for the seeder and the API's policy — and the broader rule stays right for a role a
derived project adds. Stripping a second factor is refused against oneself for another reason: it
would let a stolen administrator session drop the factor without the password the self-service
route demands. Granting a role and unlocking add access and are not guarded. Every administrative
change takes effect at once: the lock has no expiry and only an unlock lifts it, and a lock, a role
change and a stripped factor each rotate the stamp and end every session of the target.

**Each mailed token has a provider and a lifespan of its own, and any instance can redeem it.**
`AddDefaultTokenProviders` registers its providers against one unnamed
`DataProtectionTokenProviderOptions`, and Identity points password reset and email change at the
`"Default"` provider unless told otherwise, so both would inherit email confirmation's one-day
lifespan. Each is therefore a named provider with an options subtype of its own, an hour by default
(`PasswordReset:TokenLifespan`, `EmailChange:TokenLifespan`); confirmation keeps its day
(`IdentityTokens:Lifespan`). The tokens are data-protected, so the key ring is persisted in the
identity schema under one application name every `AddDataProtection` call in the process shares: a
link minted by one instance opens on another and survives a redeploy, and the passkey ceremony
states sealed with the same ring do too. Confirming an address rotates the stamp, which Identity's
`ConfirmEmailAsync` does not, because a data-protector token embeds the stamp and is otherwise
replayable. A reset token is minted whatever the address's confirmation state: a forgotten password
locked behind a confirmation link would strand its holder.

**Some authentication writes commit on their own, and that reaches the caller's unit of work.** A
grant has to be durable before its value leaves the process, so every refresh-token operation of
`ISessionTokensService` commits the request's unit of work, and a caller with other work staged in
the same request has that committed too. A rotation is one transaction of its own, replayed whole on
a transient failure: the conditional `UPDATE` that consumes the presented grant commits with its
successor or not at all, since a grant spent by a failed attempt would read as a replay on the retry,
and a commit whose acknowledgement was lost is recognised by the successor it wrote. Ending sessions
commits its own statements, so a revocation survives a caller that walks away. The spent-ceremony
`INSERT` is `ON CONFLICT DO NOTHING`, outside the change tracker, so it commits nothing the request
staged and leaves no failed insert behind for the next save to retry. Every `UserManager` write
commits on its own, which is what lets an account deletion be published the moment it returns.

**The security-event trail never names an address, and never fails the request it describes.**
Login, reset and resend answer identically whether an address exists, so a log line naming the
address on one branch would leak what the response hides: every event speaks in user ids, `null` for
an attempt against an address that named no account. Each event is written twice — a structured log
entry first, then a row of `identity.SecurityEvents` through a context of its own, in its own short
transaction. Several events happen outside any transaction, a shared context would flush the caller's
pending changes, and a row joined to the action would vanish with a rollback that is itself worth
recording. A failed row is logged at `Error` and the outcome left as it was, so the table can have
gaps the log does not. The row has no foreign key and outlives its account for
`SecurityEventLog:RetentionInDays`; its kind is stored by name, so a member added in the middle
rewrites nothing. Reads are one account at a time, the holder's own or the one an administrator
names, and an administrator's read of an id naming no account answers what remains rather than a
`404`, since a deleted account is exactly what an investigation asks about. The purge deletes in
batches of `SecurityEventLog:PurgeBatchSize`, oldest first, and takes no lease.

**External sign-in is one OpenID Connect adapter, and a provider is configuration.** Google,
Microsoft and Apple all mint an `id_token`, and checking one is the same four checks whichever minted
it; only strings differ, so a provider is an `ExternalIdentity:Providers` entry and not a class. An
empty section is valid and the default: a deployment without external sign-in boots and refuses
every attempt. An entry that could only fail at its first sign-in — a duplicate name, no key-set
address or two, a plaintext one — is refused at start-up. Names match case-insensitively, since a
client sends them; issuers and audiences are lists compared whole, because Google mints two issuer
forms and one product registers several client ids. The one provider-shaped difference is Apple's
`email_verified`, a string where Google sends a boolean, read by the same call and held by
`ExternalIdentityVerifierTests`. Key sets come through a typed `HttpClient`, so the outbound budget
applies — `JwtBearerOptions.Backchannel` and `HttpDocumentRetriever`'s parameterless constructor
build a client of their own inside the package, where `NoType_ConstructsItsOwnHttpClient` cannot
see — and the cache is a singleton apart from it, because the typed client is transient. Keys are
believed for `KeySetLifetime`, counted from the provider's last answer so an outage cannot extend
them, and a failed fetch keeps the keys already known rather than refusing every sign-in. A token
naming a `kid` the cache does not hold forces a fetch at most once a minute per provider, counted
from the last attempt: an invented `kid` then costs sixty requests an hour, and a missed rotation
sixty seconds.

**A sign-in ends in one of two typed shapes, whatever proved it.** `LoginOutcome` is a closed
hierarchy with a JSON discriminator — a token pair, or a two-factor challenge — rather than one
record of nullable fields, so a client switches on a `status` tag instead of guessing which fields
go together. External sign-in ends in the same two, because a provider stands in for a password and
never for a factor the owner armed. Redeeming a challenge and completing a passkey sign-in answer
with `LoginOutcome` too, always its token-pair branch, so a client has one contract for "a sign-in
just finished". Each of them, and every refresh, re-asks whether the account may still sign in after
its proof has passed, so a lock lands on every way in at once. What the session knows about the
account is re-read from the store as well: every access token's claims, the profile and its roles,
never the presented token's claims. A token that outlived its account is answered as
unauthenticated, not as a missing resource.
<!--#endif-->
