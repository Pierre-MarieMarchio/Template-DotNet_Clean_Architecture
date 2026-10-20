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
never widen the default.
<!--#if (files)-->
Two callers are out of the default's reach, the AWS SDK and the malware scanner's raw socket, and
restate it in their own vocabulary.
<!--#endif-->
**The 30 s total is bound to `RequestTimeouts:Default`'s 5 minutes**: an
outbound call happens inside an inbound request, so the enclosing budget has to be the longer of
the two, for the same reason that default outlasts the database's own retry budget
([`CONFIGURATION.md`](CONFIGURATION.md#requesttimeouts)). If either number moves, re-read both. The
package bounds the other side: the total must exceed the 10 s attempt timeout, and the circuit
breaker's 30 s sampling window must be at least twice the attempt, which 10 s and 30 s meet with no
margin.

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
releases the lock rather than stranding a lease until a timer says otherwise. For that the lock is
taken on a connection of its own, outside the pool: a lease is held for minutes where a pool
amortises milliseconds, and pooling it would put the session's lifetime in the pool's hands. A
name becomes the lock's `bigint` by SHA-256 truncated to eight bytes, big-endian, because
`string.GetHashCode()` is randomised per process and `BitConverter` follows the machine's
endianness — either would give two hosts two keys for one name, and the lock would serialise nothing
while looking healthy. Release is best effort and deliberately not given the caller's token: the
case that most needs releasing is the cancelled one, and closing the session releases the lock
anyway, so the call makes it go away promptly and is not what makes it go away.
<!--#if (files)-->
The per-owner quota lock of a registration is the same mechanism, waiting rather than trying, and
its name is free of the project name because the template engine renames that wherever it appears,
which would change the key and break the digest a unit test pins.
<!--#endif-->
**It is not a fencing
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

## Infrastructure

**A save is flushed, stamped and published in that order, by interceptors, because the unit of work
is not the only way to save.** EF does not map the domain types, so its change tracker cannot see a
mutation made inside an aggregate. Something has to write each aggregate's state onto its tracked
rows before EF computes its diff, stamp the audit columns on what that wrote, and collect the events
once the rows are settled. Three `SaveChanges` interceptors do it, and the order they are added in
is the order EF runs them: the flush comes first because the other two only see an entry that is
already added or modified, and `DetectChanges` is called again after it because its ordering against
save interception is a provider detail. They are interceptors rather than steps in the unit of work
so that a stray `context.SaveChanges()`, the synchronous overload included, flushes and publishes
too; a flush that only happened on the blessed path would be silent data loss. Each acts only on what
is staged on the context being saved, since a request also saves other contexts, and other instances
of the same one, in transactions of their own. Events are collected before the save and published
after the commit, so a handler never observes a write that rolls back, and publication ignores the
caller's cancellation: the write has landed. **A publish failure is not a commit failure.** The
dispatcher resolves consumers straight from the container and gives each its own `try`/`catch`, so a
consumer that throws is logged and stepped over and its siblings still run — isolation, not
durability, which the decision on event delivery above says is survivable. A failed save publishes
nothing and hands its events back to the sources they were drained from; they have already left the
aggregates and cannot be raised twice, so without that the documented retry after a conflict would
publish nothing. `IDomainEventPublisher`, for a fact raised outside an aggregate, goes through the
same dispatcher but promises no ordering: its caller owns it and publishes only what has committed.

**The commit boundary is one `SaveChangesAsync` of one named context.** `IUnitOfWork` says nothing
about which context it commits, which suits a use case and fails a deployment holding more than one:
the unnamed port can be registered once, and writes staged on a second context would be committed,
or not, by the first — silently, because saving an empty context succeeds and reports zero rows. A
module that owns a context takes `IContextUnitOfWork<TContext>`; the module owning the business
context registers the unnamed port over its own. EF wraps the save in a transaction, so what a use
case staged lands whole or not at all.
<!--#if (accounts)-->
The account stores commit by themselves, so an account creation and a domain write share no
transaction.
<!--#endif-->
A concurrency conflict leaves as `ConcurrencyConflictException`, translated at this boundary because
EF's own exception names EF and carries its entries, with the original as inner exception. It is
neither swallowed nor retried: a retry would re-apply a decision made against state that no longer
exists, which is the lost update the version exists to prevent.

**Audit columns name an actor, which is not the caller.** The host supplies `IAuditActor` as it
supplies `ICurrentUser`, but they answer different questions: `ICurrentUser` says who is calling,
which a host with no request cannot answer and must not invent; `IAuditActor` says whom to record,
which it can — nobody. Collapsing the two made every commit from a background job throw at the
stamp. An unattributed write is stored as null: null says "we do not know who did this", where
`Guid.Empty` is a lie that looks like a user id. The interceptor is the only writer of the stamps; a
mapper copying audit values out of a domain object would be a second writer, and the two would
diverge.

<!--#if (aggregates)-->
**A row is not the aggregate, and each persisted value has exactly one writer.** EF maps rows with
settable properties and no invariants; the aggregate answers to its own rules and the row to the
schema, and the mapper with the flush pipeline pays for the separation in one place. The mapper is
total in one direction — everything the store holds becomes domain state — and deliberately partial
in the other: onto a *tracked* row it assigns the domain's columns and leaves the version and the
audit stamps alone, so EF's own diff decides what to write instead of a full-row `UPDATE` that
flattens the audit columns. The version is PostgreSQL's `xmin`: never written by this process, read
back after each write, used in the next one's `WHERE`. The aggregate's version is pushed into the
row's *original* value, the one EF puts in the `WHERE`, without marking the property modified, so
the guarantee holds by construction even for an aggregate that outlived its query; the version and
stamps the store decided are copied back after a save, so a second write in the same request does
not fail against a token the first one moved. A deleted or detached entry is not written to. When
only a child row changed, its root is enrolled by touching one stamp, not by marking the entry
modified, which would rewrite every column; the root's `xmin` is then the arbiter for the whole
aggregate. A tracker — identity map, list to write, source of events — is one object per request
registered under every contract that needs it: three descriptors would give three empty identity
maps, and every write would succeed while persisting nothing. It is separate from the repository
because the flush interceptor reaches it while the context's options are being built, and a tracker
that depended on the context would close a cycle. A mapper that forgets a property loses data
without failing anything, so the reflection-driven round-trip test is the barrier, and the
persistence project shares its internals with that test's assembly and no other.
<!--#if (files)-->
For a stored file the mapper guards more: `ObjectKey` is the only record of where the bytes were put
and the orphan sweep deletes every object no row names, so a key dropped, trimmed, re-cased or
minted afresh unlinks live content with no error anywhere. `StoredFileMapperFidelityTests`,
`StoredFileMapperWriteFidelityTests` and `StoredFileMapperObjectKeyTests` are the barrier;
weakening one removes it.
<!--#endif-->
<!--#endif-->

<!--#if (notes)-->
**A feature with no aggregate stages its row directly, and the database still has the last word.**
Notes have no mapper, tracker or event source. The store loads the owner's row tracked, checks the
caller's versions against it — ownership first, so a note that is not the caller's is never reported
stale — and stages the edit. EF then writes `UPDATE … WHERE "Id" = @id AND xmin = @loaded` (the
`DELETE` likewise), and a row the database no longer finds at that `xmin` is a rejected write that
the unit of work reports as a conflict. When the caller sent `If-Match`, the version in that `WHERE`
is one it named.
<!--#endif-->

<!--#if (tagging)-->
**Child rows are entities with composite keys, not owned collections.** Owned types are loaded and
reconciled implicitly, which is convenient until reconciliation is something this layer does by hand
and implicit behaviour is what makes it hard to say which rows were written. A tag is keyed by its
owner and its value, not a surrogate: it has no identity of its own, so a duplicate is impossible in
the database rather than merely unlikely, and reconciliation is a set difference that leaves values
present on both sides untouched. A repository loads each collection with its own query, because one
join returns root × items × tags and a hundred items with five tags would arrive as five hundred
copies of the root row.
<!--#if (todoLists)-->
Items reconcile by id: matching by position or title would turn a rename into a delete and an
insert, taking the row's foreign keys with it. They have no `DbSet`, so the only way in is through
the root, as in the domain; and there is no unique index on `(TodoListId, Title)`, because the
domain rule is case-insensitive and a B-tree index is not, so it would enforce a weaker second rule
that the aggregate, which always loads every item, already enforces.
<!--#endif-->
<!--#endif-->

<!--#if (anyFeature)-->
**Keyset paging walks ties in the direction it orders them.** Each sort map folds the terms into
`ORDER BY` and appends `Id` as a unique last key, so equal sort values cannot swap between pages. The
tiebreaker runs in the direction of the last term: a cursor resumes with `Id < id` when descending,
so a fixed ascending tiebreaker would order the tied rows one way and walk them the other, skipping
every tied row past the cursor. It also keeps `(OwnerId, key, Id)` scannable backwards, where a mixed
direction would need an index of its own. Each sortable field has one index leading with `OwnerId`,
which every read filters by, and ending in `Id`; a field made sortable is a field that gets an
index. A nullable column gets no keyset arm, because comparing with `NULL` would skip the cursor's
own row. The value the next cursor carries is read from the projected summary, never from a record
assembled by hand: a field made keyset-sortable later would mint a cursor from default values,
silently, and reading the summary turns the same mistake into a compile error. Every default arm
throws, because the use case has already refused anything off the whitelist and reaching one is a
defect that must stay loud; a multi-term sort is refused under cursor paging, so one term is all a
page ever sees.

**A query names the owner in its predicate, and the one that does not says so.** Fetching by id and
comparing the owner afterwards would read another user's row first and make a missing row
distinguishable from somebody else's; one query with the owner in it answers both the same. The
version is selected by the same statement as the representation, so it describes exactly the row
that was read, where a second query would leave a window in which the two disagree. Each feature has
one query that crosses owners on purpose — which ids still hold data, for the housekeeping sweep —
and it returns ids only, with the primitive built client-side because its factory validates and
validation is not SQL. A free-text search escapes `%` and `_` before building its `ILIKE` pattern,
which is a safety property: unescaped, a caller's `%` matches every row. The escaping helper is
copied per feature until a third one needs it, so deleting an example cannot break another feature's
search.
<!--#if (files)-->
A file is searched by name only: a searchable object key would let a caller probe for others'
objects.
<!--#endif-->
<!--#endif-->

**Writes the request cannot own commit through a context of their own.** The idempotency store
claims, completes and releases keys on short-lived contexts built from the same connection options,
because a claim is not part of the business transaction: it must survive whether the use case that
follows commits, and releasing it after a failed action must not roll back a domain write that
already landed. A context the factory creates must be created with no options action of its own — an
action would replace the pool-bounded connection string with the raw one and give those contexts a
second pool — and the factory is scoped, because the default singleton would depend on scoped
options, which scope validation refuses. A context of its own also avoids the failure the shared one
has: a refused insert stays tracked as added on a shared context and is retried by the next save.
Writes are single statements, or two in one transaction, replayed whole by the retrying execution
strategy; an insert that committed but whose acknowledgement was lost is retried, meets the unique
index, and is answered from the row it had in fact written, read back rather than recognised by a
provider's error code, so the decision is the same on every provider.
<!--#if (externalAuth)-->
The subject-to-owner mapping is written while the request is being authenticated, so it commits on
its own: on the request's context a failed operation would roll back a mapping that is true
regardless. Its refresh is one `UPDATE` that never loads the row and never names the owner in its
`SET`, so no refresh can move a subject to another owner; deleting an owner and its roles is two
statements in one transaction.
<!--#endif-->
<!--#if (files)-->
The orphan sweep's checkpoint commits at once for the same reason, outside the scope's unit of work.
<!--#endif-->

**An idempotency claim is a lease, and the primary key is what makes claiming race-safe.** The key is
the pair `(UserId, Key)`, not a surrogate, so two concurrent claims collide on it and the loser is
told what the winner decided instead of both believing they own the claim. `ClaimedUntil` is the
lease on an *unfinished* claim and `ExpiresAt` the retention of a *completed* response: a claimant
that dies between claiming and completing would otherwise leave the row stuck for the whole
retention window. Past the lease, a retry reclaims the row with one `UPDATE` that restates every
condition that made it reclaimable, so the database decides which of two simultaneous retries wins;
a different fingerprint is the same key reused for another request, a client error and never
reclaimable however stale. A null body means two things: on a 204 it is the response, on anything
else the body was too large to keep and replaying it would be a lie, so the claim answers
`idempotency.notReplayable`. The purge deletes in bounded batches ordered by the indexed `ExpiresAt`:
one `DELETE` over a range of hundreds of thousands of rows holds its lock for the whole scan and
leaves a single burst of dead tuples, and a batch smaller than the limit is the only way to tell the
last one from "there is more" without another round trip.

**The database is one, each feature owns a schema in it, and the pool is bounded once.** Every
context reaches PostgreSQL through the one connection string name `Default`; a second name would be
a second thing to configure, and the two could disagree unnoticed until runtime. The halves of the
system separate themselves by schema and by migrations history, never by connection. A feature's
tables live in its own schema, so removing the feature is a deleted migration file rather than a
drop; the tables no feature owns live in `platform`, because a feature's schema would not be honest
for them. The migrations history table stays in the default schema, named at runtime and in the
design-time factory alike so the tool and the process cannot disagree about what has been applied.
The design-time factory falls back to the local docker-compose database because one that threw would
make `migrations add` impossible on a clean checkout; it only picks a provider and never reaches a
real server. `DatabaseOptions` bounds the pool in one place: Npgsql pools per connection string and
defaults `Maximum Pool Size` to 100 per process, where PostgreSQL's `max_connections` is 100 for the
whole server, and an idempotent write can hold up to three connections at once — so the default is
well under 100, and [`CONFIGURATION.md`](CONFIGURATION.md) says how to size it against replicas.
Each context's configurations are listed in the model one by one, not found by scanning: the
compiler checks the lines, where a scan silently maps nothing when a configuration is renamed or
moved.

**There is one clock, it reads UTC, and the context has no business stamping.** `IDateTimeProvider`
is registered once in infrastructure because reading the machine's time is an interaction with the
outside world, and two features each supplying one is how a system ends up with two notions of
"now". Stored instants are UTC: a column mixing local and UTC values makes every comparison, and
every migration to a server in another zone, silently wrong. The context itself takes no
`ICurrentUser` and overrides no `SaveChangesAsync` to stamp, flush or dispatch: the first would make
every instance depend on an HTTP request, and an override would miss the synchronous overload that
the interceptors cover.

**The cache is `HybridCache`, in process until a deployment says otherwise.** The template runs on
`dotnet run` with nothing to deploy; a deployment that wants a shared second level registers an
`IDistributedCache` beside it and no caller changes. Both expirations are the same lifetime, since
the local copy outliving the shared one would hand back a value the rest of the deployment has
already dropped.

**`AppTemplate.Infrastructure.Core` exists because an infrastructure module may not reference a
sibling.** Without it two modules needing one mechanism have nowhere to share it and each keeps a
copy. It holds mechanisms a module composes over its own context — the unit of work, the three
interceptors, the tracker, the templating, the clock — and no database provider: which database a
context talks to is the module's decision. A module composes a mechanism by making the registration
call, never by naming the adapter, which stays internal;
`AdapterVisibilityTests.Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule` holds it,
and it is what lets two modules share one mechanism without either owning it.

**A mail's subject and body come from one embedded template per language.** The subject is the
template's `<title>`, not a separate setting, so a subject and a body cannot be delivered in two
languages. Templates are embedded resources named `<baseName>.<culture>.html`, declared
`WithCulture="false"` or MSBuild compiles them into a satellite assembly the renderer cannot see;
shipping a language is adding a file. Rendering picks the closest available culture — `fr-CA`
reaches `fr` — then the fallback, and a missing fallback template is an exception, since a reader
whose language is unavailable would have nothing to receive. Placeholders are `{{Name}}` and every
substituted value is HTML-encoded, so a value a user chose cannot carry markup into a mail; the
subject takes none, because a mail header is not HTML and an unencoded newline in one is header
injection.
<!--#if (reminders)-->
A background pass has no request to read a reader's language from, so the reminder mail is written
in `Localization:DefaultCulture` until an account carries a stored preference;
[`CONFIGURATION.md`](CONFIGURATION.md) says where that plugs in. Its instant is formatted
round-trip, since the process builds with `InvariantGlobalization` and there is no localised long
date to reach for.
<!--#endif-->

**Mail leaves by exactly one of two transports, chosen once by configuration, and nothing falls
back.** `Email:Transport` is `Smtp` when absent, which is what every existing deployment runs, and
an unknown value fails composition: a fallback would send a deployment's mail over a transport
nobody asked for, and a typo in that key is how it would happen. Only the chosen transport's
options are bound and validated at start-up, so a deployment sending over HTTP owes no relay
settings and one sending over SMTP owes no API credential. The transport is read from configuration
rather than from `IOptions` because it decides what gets registered, before any provider exists.
Both transports are in one project, one folder each: the port already lives in
`AppTemplate.Application`, a project pair would put exactly one implementation on each side of a
second abstraction, and the second transport arriving as a folder is the demonstration. The HTTP
transport adds no package — a provider needing an SDK would be an argument against that provider —
and `Microsoft.Extensions.Http` is referenced explicitly rather than taken from the ASP.NET Core
runtime, so a generic host is not made to require it. SMTP defaults to mandatory STARTTLS, and any
mode that can fall back to plaintext is refused for a non-loopback host unless
`AllowInsecureTransport` is set; `Auto` is on that list because MailKit resolves it to
`StartTlsWhenAvailable` on every port but 465. The opt-in exists for containerised relays such as
mailpit and makes an insecure transport a visible choice in configuration. Postmark's counterpart is
a base URL that is plaintext to begin with, where what travels is the credential that sends every
message, so plaintext HTTP is refused beyond loopback and has no opt-in. **Nothing retries a send**:
the outbound default replays only safe verbs and Postmark's endpoint takes no idempotency key, so a
replay after a timeout is a second delivery the recipient can see; a failure travels as an exception
to a caller who owns recovery. The client is typed and named after its adapter so the log categories
and resilience metrics say which transport failed. No `RedactLoggedHeaders` call is made: the
factory's logging handler redacts every header value unless handed a list, and a list replaces that
default, so naming the server token would un-redact the others. The token goes on the request, not
the client's default headers, so a rotation is not pinned at composition time. Error messages carry
the status and Postmark's error code, never the credential or the message body. A base address whose
path lacks a trailing slash loses its last segment when a relative URI is resolved against it, so
the module appends one.

<!--#if (files)-->
**Bytes are the object store's to keep or refuse, and this process never takes less than the
store's word.** Both stores bind an object to one declared digest and size. S3 receives one
`PutObject` carrying length and SHA-256, so the store keeps the whole object or nothing, and no
total budget applies to a write, which lasts as long as its body. The file system store writes to a
temporary file while counting and hashing, and publishes only a match, by a rename that cannot
overwrite; an existing key keeps its object. Temporary files start with a prefix no object key has,
so the inventory never lists them and deletes those a crashed write left behind. A client-supplied
name never reaches the disk, and a path is refused unless strictly under the root. Describing an
object returns the digest the store recorded at write time — computed by reading the file on the
file system — and an S3 object without one is a fault, not a reason to answer another digest that
would fail every confirmation.

**A grant is a bearer right, so it is signed for the host the client uses, for exactly the bytes it
was asked for, and for no longer than the operator allows.** A Signature Version 4 URL covers its
host, so presigning uses a second client built on `PublicEndpoint`, the name the client will reach;
the first client talks to `Endpoint`, the one this process reaches, and the pair differs whenever a
store has an internal and an external name. The digest is signed into an upload, so the store
refuses other bytes, and every header set is signed, so a deposit sending anything else is refused.
A download signs its response headers, so the client cannot turn an attachment into something
rendered. The lifetime is clamped to `MaxGrantLifetime`, whose default is the longest window a use
case asks for; anything longer is a write right left in logs. The ceiling holds to the signer's
clock precision — the SDK reads the clock again and rounds `X-Amz-Expires` — so the store's
deadline may differ by about a second, and it signs from the machine's clock, not the injectable
one. Plaintext HTTP is allowed only for the signing endpoint, behind `AllowInsecureTransport`, and
never for what this process reaches: a signed URL is a reusable bearer right that would travel in
clear to the client, where the server-to-store call carries per-request signatures only. The
signing endpoint's scheme wins, because the SDK defaults to HTTPS whatever the endpoint says.
Credentials are optional: absent, the SDK's chain supplies an instance role, and exactly one of the
two keys is a process that starts and signs anonymously.

**The scanner gives a verdict or says it could not, and it never guesses clean.** `clamd` speaks a
line protocol over a raw socket, so the outbound HTTP policy cannot reach it, and neither can it
reach the AWS SDK, which builds its own `HttpClient`: `ScannerBudget` and `BucketBudget` restate the
policy in socket and SDK vocabulary with the policy's numbers, and **if they move in
`OutboundHttpExtensions` they move there**. The scanner has no retry — it would stream the object
past the daemon again, and the periodic pass offers the same file on its next tick — and its total
budget also bounds the scannable size: raising `MaxScannableBytes` without raising the budget makes
a large scan a cancelled one, reported as no verdict and retried for ever. An unreachable or
misbehaving daemon is "no verdict", never an exception, and a reply that is not `OK`, `FOUND` or
`ERROR` is no verdict, because the only guess would be "clean". The content is sent and the verdict
read concurrently: `clamd` answers on detection and then resets a connection with unread bytes,
which would lose a verdict read only after sending. Archive limits (`MaxScanSize`, `MaxFiles`,
`MaxRecursion`) are the daemon's and a deployment must set them. Scanning ships disabled: a no-op
scanner finds nothing, the signature check on the leading bytes still runs, and
[`SECURITY.md`](../SECURITY.md) records the missing antivirus verdict.
<!--#endif-->

**The in-memory module replaces adapters by removing them, and its doubles fail in no way of their
own.** `AddInMemoryModule` removes and re-adds rather than relying on the last registration winning:
a double that worked only when it happened to be registered last is a silent dependency on
composition order, and the failure is a test that quietly exercised the real SMTP client. It
therefore takes no configuration, and is called after the modules whose adapters it replaces. A
double does not throw, queue or pretend to fail, because a double that simulates failure modes
accumulates a second implementation of the thing under test; a test needing a failing adapter
substitutes one for the single call it cares about. Recording doubles are public and resolvable,
since recording is useful only if a test can read it back, and internally locked, because an
integration test sends from several requests at once; the adapters stay internal so a test asserts
on the port and not on the double. The fixed clock moves only when told to and rejects a negative
step: a clock that runs backwards makes an expiry check pass twice, a test that proves nothing.
<!--#if (files)-->
A deposit is something a test performs and never something issuing a grant performs, because the
grant issued without a deposit is the case confirmation and the abandonment sweep exist for. The
store, the direct transfer and the inventory share one `StoredObjects`: the sweep lists what the
store holds and deletes through it, so two instances would make it reclaim nothing while appearing
to work. Minted URLs are HMAC-signed under a per-instance secret, so a grant is a bearer right here
too, and they resolve nowhere.
<!--#endif-->
<!--#if (externalAuth)-->
The identity verifier is the one port a test could not reach otherwise — presenting a token the API
would accept means holding a provider's private key — so what a token means is arranged by the test,
and the checks the real adapter makes are proved against real signatures in its own unit tests. An
arrangement is keyed by provider and token together, because a token is meaningful only for the
provider it was presented for.

## Unit tests of the domain and application

**A limit is asserted against a literal, once.** The length of a value, the cap on a collection, the
width of a generated key and the ceiling on a password are each stated in one test as a number,
because every other boundary test reads the constant and moves with it. Widening a column, a cap or
a key then fails a test and has to be decided, instead of being a smaller diff than it deserves. A
lease name is pinned the same way: every replica derives its lock key from it, two releases run
side by side during a rolling deploy, and renaming the constant would silently split the leader in
two.

**A test may pin what a rule deliberately leaves unchecked.** Where a check was left out on purpose,
a test says so, so that adding it is a decision that fails something and not a tidy-up. A validator on
an anonymous route measures presence only and leaves the rest to the verifier or the store, because
a 400 would tell a caller which addresses or providers exist. A load path does not compare two
instants that a clock correction can reverse, and does not re-check a precondition that time
breaks. A parser is looser than the minting that feeds it, so the keys of yesterday's scheme still
load.

<!--#if (aggregates)-->
**Rehydration is tested as an act of its own.** Loading takes the id it is given, raises no event,
and goes through the same factories a live aggregate does, so a row that no longer satisfies an
invariant is refused on the way in. It refuses what no sequence of operations on the aggregate could
have written — a state and its confirming instant that disagree, stated as an equivalence over the
whole enum and not as one clause per state, so a member added later is not exempt from both. It does
not refuse what only time makes false: a reminder stops being due in the future by the mere passing
of time, and a load path that re-checked it would throw on exactly the rows it exists to load.
<!--#endif-->

<!--#if (aggregates)-->
**Test doubles keep the real behaviour wherever a guarantee depends on it.** `ATodoList`,
`AReminder` and `AStoredFile` build real aggregates, never fakes: a root that accepted anything
would make "a duplicate title becomes a conflict" assert nothing. Where a state cannot be reached
through the aggregate's own methods — a version, an overdue reminder, a quarantined file — they load
it as a store would. The in-memory stores keep the two properties of the real one that a delivery
test depends on: a save writes every changed aggregate or none, and a write against a version that
has moved is refused and stays staged, as it does in the EF context. That is why a use case cannot
step over a conflict and carry on in the same scope, and it is the level of proof: a use-case test
with a faithful double, not the SQL that orders and filters the real query, which the integration
suite covers and which runs only where Docker does. A cache that records is a real one, not a
substitute whose `GetOrCreateAsync` answers `default`.
<!--#endif-->

<!--#if (files)-->
**The file aggregate's tests assert the design, not only the code.** `Register_DoesNotDeriveTheObjectKeyFromTheId`
and `TimeSegmentFor_IsComputedInUtc` hold what the orphan sweep is correct because of: a key is
generated, and the slice it sits in is a function of the registration instant read in UTC, so the
sweep never finds a live file's bytes unreferenced. `TheStateMachine_HasNoStateMeaningDeleted` and
`TheStateMachine_KeepsTheNumbersAlreadyWrittenToRows` hold two things nothing else in the build can
see: a deleted file is a removed row, not a state a query has to remember to filter, and the state is
persisted as its integer, so renumbering a member would reread every row as another state with no
migration generated. `MakeAvailable_Rejects_AQuarantinedFile` holds the guard on the aggregate and
not on the caller, so no second pass, retried message or future route has to remember it. A deposit
confirmed is not a file released, and neither step raises an event: an event there would invite a
consumer to inspect the content inside the request the inspection is arranged to stay out of.
`Quarantine_LeavesTheObjectKeyNamingTheSameBytes` holds that refusing a file removes nothing, so the
bytes stay reachable to an investigation and deletion stays the one path that reclaims them.
<!--#endif-->

<!--#if (files)-->
**The content policy refuses by what the bytes say, and its limits are tests.** A format named
by its own signature that differs from the declared type is quarantined, and so is a declared type
that always has a signature when the content has none. Markup is refused in every category, an
honestly declared SVG and an honest XML data file included (`AnHonestlyDeclaredSvg_IsQuarantinedToo`,
`AnHonestXmlDocument_IsRefusedToo_BecauseNothingHereSanitisesMarkup`): nothing here sanitises it
and the download path hands out a URL to an origin the application does not control, so the format
is the only thing this layer can refuse. A project that has to accept XML changes those tests
deliberately and owes a sanitiser. The markup search reads a truncated head, decodes UTF-16 by
dropping its null bytes and looks past a byte-order mark; a second check refuses an SVG whose
markup was padded past the head. ZIP has no entry in the signature table because every Office,
OpenDocument and EPUB file starts like one, and an archive declared as an image is still refused by
the other direction. There is no alias table, a second place for the two sides to disagree: a
client that spells a type the common wrong way is refused and told the canonical one. A format with
no signature — CSV, JSON, plain text — cannot be checked this way and is released. No verdict is
neither a pass nor a refusal: an outage is checked before anything else, so an empty head can never
be read as content that failed the type check, and the file is asked about again.
<!--#endif-->

<!--#if (localAuth)-->
**Anonymous routes are tested for sameness, not only for refusal.** Every login refusal answers
one error down to the message, asserted against a literal and again against each other: a change that
moves them all to one new error is caught by the first, one that splits them by the second
(`NoRefusal_IsDistinguishableFromAnother`). A known address and an unknown one, a reachable relay
and an unreachable one, a wrong code against a live challenge and an unknown challenge are each
asserted indistinguishable from the answer alone, while a cancellation still propagates, since it is
not an outage. An external identity resolves by the pair of provider and subject and never by
address once it is linked, so a provider that sends an address on the first authorisation only keeps
signing in; a first link needs a verified address and a confirmed local account.
The use cases that rotate the security stamp revoke the refresh tokens themselves, because those
survive the rotation, in one call naming the session to spare — none when the token names none.
<!--#endif-->

## Unit tests of the adapters and the API

**A mapper test proves the mapping is total by walking the aggregate's type, and proves the walk can
fail.**
<!--#if (aggregates)-->
The fidelity tests of the aggregates' mappers enumerate the aggregate's readable state by
reflection instead of listing it, so a property added to the aggregate and forgotten by the mapper
fails the round trip. The sample differs from its counterpart in every domain-owned value, because a
value compared with itself passes for a property nobody copied; it keeps the same ids, because a new
id would turn the update into an insert and skip the path the sample exists to exercise.
<!--#endif-->
A list of members the walk skips is kept per test class and names each one for a reason: the domain
events are never persisted (they are raised, published after the commit and dropped, and their
survival is the integration tests' guarantee that a consumer ran exactly once), the version is
PostgreSQL's `xmin` and has a single writer, and the four audit values are the interceptor's and flow
from row to aggregate only. `TheExclusionList_NamesRealMembers` keeps the list from rotting into a
silent exemption, and `TheHarness_DetectsAMapperThatForgetsAProperty` runs the walk against a mapper
with one line forgotten, since a green test nobody has seen go red is not evidence.

<!--#if (localAuth)-->
**The identity adapters are tested over a real `UserManager` on substituted stores, and the stores
are set up for what `UserManager` does around them.** NSubstitute answers an unconfigured
`Task<IdentityResult>` member with a null result and `UserManager` reads `.Succeeded` off it, so every
test that reaches a store write configures the update to succeed. `UserManager` also normalises an
address or a role name before it reaches the store, so a lookup is stubbed on any argument and a
seeded role is stored upper-cased. The token providers are registered under the name `UserManager`
resolves, with a stand-in that carries the one property the test relies on: a token is only as good
as the security stamp it was minted against. The in-memory account rows seed a security stamp because
`AddLoginAsync` rotates it, and carry the unique indexes of the real schema but no duplicate check on
a login, which `UserManager` performs itself; repeating it there would make a test pass through a rule
the production path never reaches. A security-ordering assertion is made before the next call is
attempted when the order is the guarantee: `RedeemChallengeAsync_AfterTheConfiguredNumberOfWrongCodes_DestroysTheChallenge`
checks that the write which reached the ceiling destroyed the challenge, not that the next call
refused it.

<!--#endif-->
**A test double is written by hand when a substitute would hide what is under test.** The recording
loggers keep the formatted line because `ILogger.Log` receives its state as a generic parameter and a
substituted call is unreadable. The recording dispatcher of the persistence tests is hand-written
because the dispatcher port is internal to its assembly and a dynamic proxy would need that assembly
to trust the proxy generator, a wider grant than a test double is worth. A misconfigured service
provider is hand-written in `DispatchAsync_StillThrows_WhenARegistrationIsNotAConsumer` because no
ordinary container hands the dispatcher a registration that fails its cast, and that is the only way
to show the per-consumer isolation still lets the exception through.

**Instrument tests assert with `ShouldContain` on a concurrent queue.** The meters are static and the
test classes of an assembly run in parallel, so a listener receives measurements recorded by other
tests; a plain list would lose items under two writer threads, and an assertion over everything
recorded would fail on someone else's. A zero measurement is only meaningful because no other fake in
the project reports a success with nothing removed, so a zero can only be the test's own pass. The
disabled and failing cases are asserted as well as the successful one, because a flat counter must
never be the only thing an alert sees.

**A configuration test writes down the budget it asserts.** The outbound HTTP policy asserts the
attempt, total and sampling values and the retry shape although they coincide with the package's
current defaults, so that a dependency reached on a budget nobody wrote down does not change when the
package's defaults do; the sampling window sits at exactly twice the attempt timeout with no margin,
so raising one value alone fails a test rather than a deployment. The retry delay is the only value
overridden, and only so the retried case finishes.

**A validator test records where its rule stops.** `Validate_AcceptsAFromAddressWithNoDomainBecauseTheParserReadsItAsAMailbox`
pins that a bare address boots because MimeKit reads it as a mailbox, a start-up check passing on
something no relay will accept. The culture check of the localisation options is a check of shape
only, because the project builds with invariant globalization and the runtime cannot be asked whether
`fr` is a real culture. An optional feature whose absent configuration stops the host is a regression
for everyone who does not use it, so the validators of optional sections also assert that the empty
or disabled configuration is accepted.

**A contract test says what it does not prove.** The serialisation tests of the authentication
responses declare the value as the polymorphic base on purpose, which is what puts the discriminator
in the JSON; they prove the shape of the contract and nothing about the starting type MVC serialises
with, which comes from `ObjectResult.DeclaredType` and is held by `OkOrProblem_NamesTheDeclaredType`
and by the integration tests that read the wire body. A controller test that depends on the
framework's own decision runs the result through MVC's executor (a 302 against a 307 is chosen from
two flags together) and takes an entity tag from a response, the way a client does, so the encoding is
never restated in a test.

<!--#if (files)-->
**The storage adapters are tested for what no socket can show.** A presigned URL carries everything it
authorises in its query string, so the tests read the URL apart: the grant's required headers are
exactly the signed set, the host is the public endpoint, and the credentials are fabricated because a
signature is a keyed hash that any key produces well-formed. The presigning client and the client this
process talks to differ by host name alone, so swapping them compiles and passes every assertion about
a grant;
`TheStore_TakesOnlyTheClientThisProcessTalksToTheStoreWith` and `CreateForSigning_OpensNoConnectionToTheEndpointItSignsFor`
hold the two halves. The ClamAV tests run against a daemon written in the test that reassembles the
`INSTREAM` framing, since a fake that ignored the framing could not notice a length written the wrong
way round.

<!--#endif-->
**The email adapters are tested for what they must not reveal or repeat.** The server token of the
HTTP transport is distinctive so that "appears nowhere" assertions can fail, and
`SendAsync_LeaksTheServerTokenIntoNeitherTheLogNorTheException` looks in the refusal path, the only
one that formats anything. A refused send is attempted exactly once, `SendAsync_AttemptsARefusedSendExactlyOnce`,
because the provider's endpoint takes no idempotency key and a replay is a second delivery. The
factory's header redaction narrows only when something is added to its list, so
`AddEmailModule_NarrowsNothingAboutTheFactorysHeaderRedaction` asserts the absence of that call and
`TheRedactionRule_IsSensitive_AndSeesARedactionSetNarrowedToOtherHeaders` composes the mistake to show
the assertion can fail.

## Integration tests

**One container, one host, one collection, and the classes run one after another.** Starting
PostgreSQL and booting the API costs seconds, and nothing a test does depends on either, so
`ApiFixture` starts both once for the suite. What is per test — the rows, the recorded mailbox, the
captured log, the clock — is reset by `IntegrationTestBase` before each test, and every class joins
the one collection so those resets cannot race: truncating every table between tests would delete a
concurrent test's rows. The reset is a truncation and not a rollback because a test drives several
requests, each with its own scope, context and commit, so no ambient transaction exists to roll back.
`TestDatabase` discovers the tables of the schemas it is given and names the schemas by hand: a
schema nobody lists is never reset, and its rows leak into the next test. The migrations history
tables and the Data Protection key table stay out of the reset, because truncating them makes the
next run re-apply every migration, or pulls the key ring from under a host that keeps it in memory.
Teardown disposes the container with `CancellationToken.None`, because a cancelled run is the one
that most needs the container gone.

**The test host takes its configuration from environment variables, and sets every key a validator
requires.** `Program.cs` reads configuration while it composes the container, before the host is
built, and environment variables are in the default chain and outrank every `appsettings*.json`, so
they are visible at that moment whatever the content root resolves to. A section is set even when
`InMemoryModule` replaces the adapter that reads it, because `ValidateOnStart` does not care which
adapter ends up consuming it, and a missing key stops the host before the first test instead of in
the test that needs it. The background jobs run under the role switch that disables their timers,
since the suite runs one pass of a job on demand and a timer would race the assertions.

**The clock is set to real time at the start of each test and never moves on its own.** Access tokens
are stamped from the injected clock, but the bearer handler checks `exp` and `nbf` against the machine
clock with zero skew, so a clock parked in the past mints tokens already expired. A clock that is set
once stays exactly comparable with a stored timestamp, which the auditing test relies on; the value is
truncated to milliseconds so that it compares equal with what PostgreSQL keeps.

**Each client gets a rate-limit partition of its own.** Under `TestServer` the remote address is null
for every request, so the whole suite would share the ten-request budget of the authentication
endpoints and fail with `429` for reasons unrelated to what it asserts.
`TestClientAddressStartupFilter` puts a header-chosen address in place ahead of the rate limiter, a
startup filter being the only hook that gets in front of a pipeline a test cannot edit, and
`CreateClient` hands out a fresh address per call. A test that needs several requests to share a budget
reuses the client, or pins one with `CreateClientWithAddress`. The limiter itself is untouched, and its
window is widened to hours because it has no injectable clock and would otherwise replenish between two
requests of a slow run.

**The product's `401` says nothing about why; the test host says it in a header.** An expired token, a
wrong signature and a revoked security stamp are indistinguishable to a caller on purpose, and a test
asserts exactly that. The cost is that an unexpected `401` is undiagnosable, so `ApiFactory` publishes
the reason in `X-Test-Auth-Failure`, in the test host and never in the product.

**Responses are read as text and parsed, and two problem documents are compared without their
`traceId`.** The error responses are `application/problem+json`, and a failed assertion should quote
what the server sent rather than report a deserialisation exception. `ApiJson` reads raw bodies for that
reason, and raw JSON is also how the wire shape of a polymorphic response is proved.

**A test reaches storage through a port or through SQL, never through a persistence type.** The
persistence models are internal and EF does not map the domain types, so a test that queried a `DbSet`
would assert on storage rather than on what the application layer sees, and would pass even if the
mapper stopped handing values back. `LoadTodoListAsync` loads through the application port inside a
scope of the caller's own, which exercises the query, the mapper and the identity map together;
`TestDatabase.QueryAsync` and `ExecuteAsync` cover the few assertions and seeds that have to touch a
row, such as the role row an administration test needs, since this host seeds no account data.

**A race is made a fact with `Rendezvous`, never hoped for with `Task.WhenAll`.** An asynchronous call
that never yields runs to completion before the second starts, and the race becomes two sequential
operations. `Rendezvous` holds every participant until the last has arrived, so each is known to be past
the read and not yet at the write. The same reasoning puts the lease and quota-lock tests on a real
PostgreSQL with one composition per participant: the exclusion is the session-scoped advisory lock, and
a substitute asserts whatever it was written to assert. A cap on an attempt that must not queue is not a
performance budget; it is set far above what a loaded machine needs so that only an implementation that
genuinely waits for the holder can reach it, and it turns a deadlock into a failure. Lock names are
unique per test because the lock is server-wide.

**A test that asserts a refusal carries a control, and a test over a surface enumerates it.** A refusal
test is paired with the same call succeeding, or with a read showing nothing changed, because every
assertion otherwise holds for an endpoint that refuses everyone. The tests that claim a property of a
whole surface — every action refuses an anonymous caller, every `Idempotent` action is a `POST`, every
route taking an id answers `404` to a stranger — read the controllers by reflection and assert the
count, so an action added later fails there instead of passing untested.

**An integration test of a disclosure asserts what a caller can observe.** Whole-response equality with
the `traceId` and the echoed id factored out is how "not yours" and "does not exist" are shown to be one
answer, and how a known and an unknown address are shown to be indistinguishable. A `404` is asserted
where a `403` would reveal that the id names something real.

**The OpenAPI document is asserted on its content.** A document endpoint fails quietly: the route
matches and the body is valid, and the only evidence of a defect is a missing path or a sentence with a
hole in it. The title is asserted because the test host's entry assembly is the test project, so a title
read from the entry assembly would differ between the two hosts and no other test would notice.

<!--#if (localAuth)-->
**The identity round trips live in a project of their own.**
`AppTemplate.Infrastructure.Auth.IntegrationTests` exists because the refresh-token rotation guarantee
is a property of the database — a conditional `UPDATE` that exactly one of two simultaneous
presentations may win — and a fake store would assert only that the fake agrees. It is kept out of the
API suite because that suite shares one host and one container through a collection, and a shared,
serialised context is what would stop the race from happening. The module's types are internal apart
from `AuthModule`, so the project holds `InternalsVisibleTo` from it, and needs none from the
persistence project: grants are reached through the public port and rows are inspected over the
context's own connection. `GrantTableFixture` composes the persistence module as the identity module
does, and `DatabaseReadiness` waits for a container's database to accept a connection before migrating,
because the official PostgreSQL image restarts itself once while initialising and four containers start
at once across this solution; that is the cause the evidence points at, which is worth knowing before
anyone deletes the wait as ceremony. The wait is on a fixture precondition, so a real refusal still
fails at its ceiling and names the last error.

**Passkey refusals are proved with a verifier that accepts everything.** Two kinds of test share the
ceremony endpoints. `SoftwareAuthenticator` signs real ES256 assertions, so Identity's own handler
verifies them. `AcceptingPasskeyHandler` accepts any ceremony it is handed, so a refusal can only be the
sealed state's: tampered, issued to another account, sealed for the other ceremony, or spent. It reports
a counter of zero every time, as a synced passkey does, so nothing but the state's single use stands
between a replay and a session.

**A credential change is proved by replaying the token that was in circulation.** Rotating the security
stamp is what ends an access token before it would expire, and `SetLockoutEndDateAsync`,
`AddToRoleAsync` and `RemoveFromRoleAsync` do not rotate it on their own, so each administrative action
is tested by presenting the same access token again and reading the reason from the host's header. A
refresh token is the codebase's own and survives a stamp rotation untouched unless the use case revokes
it, which is why its revocation is a test of its own. The account deleted outright is the exception: the
bearer handler looks the account up before it compares a stamp, so no stamp has to rotate.
<!--#endif-->

<!--#if (files)-->
**The object-store suite runs against the real store, and lives in the API integration project.**
`ObjectStoreFixture` starts the image the development stack runs, same release and same digest, and
composes the storage module as a host does, so the adapters are exercised through their ports. The suite
sits here rather than in a project of its own because a test project is one more entry in the solution
and one more identifier to keep in step, and because this is the integration project that already
references the storage module and whose name promises a layer rather than a single module. Only a real
store can say whether a signed grant is one it accepts, so a deposit is refused by the store itself when
its digest or its signed headers disagree, a listing is walked page by page against a boundary that falls
between two keys, and a refusal is asserted as a refusal rather than as one status, since one store
answers `400` where another answers `403`. An expired grant is waited out by asking until the store
refuses, not by sleeping a guessed interval, and the loop ends on the refusal itself so that a store
under start-up load answering something else does not end it early.

**`Files:Storage:S3:PublicEndpoint` was measured against the real store before it was believed.** A
presigned URL covers the host it was signed for, so a request arriving under another name is refused by
the same store, on the same port, for the same object. `PublicEndpointTests` shows that and the other
half: signing for the public name makes the store accept the identical request under that name, with the
connection still going to the address the machine reaches and only the `Host` header carrying the public
name. Calls the process makes itself keep using the internal endpoint, the only one it can resolve. The
fixture sets the published endpoint to the address the machine reaches, which is a property of the test
host and not of the template, and an empty public endpoint has to behave exactly as if the setting did
not exist.
<!--#endif-->

## Presentation and API

**The API reference is the API's XML documentation, read where the host calls `AddOpenApi`.**
`Microsoft.AspNetCore.OpenApi` turns XML comments into the document's summaries and descriptions
through a source generator that intercepts the `AddOpenApi` call, so it sees only the comments of
the assembly making that call: `Program.cs` makes it, once per version group, and moving that loop
into a referenced project would drop every description and still compile. What it reads is written
for the API's client and kept to one sentence: a `<summary>` on each controller action, and a
`<summary>` and `<param>`s on the request and response contracts. Asp.Versioning's AV0029 and
AV0030 are suppressed in `AppTemplate.Api.csproj`: they ask for `Asp.Versioning.OpenApi`, which
yields the same paths and schemas but replaces that generator with a transformer that renders a
`<see cref>` as an empty string (10.2.3). Revisit the suppression when it resolves a cref to the type
name.
<!--#if (localAuth)-->
`OpenApiDocumentTests` holds the rendering on `ResetPasswordRequest`'s description.
<!--#endif-->

**Every project generates its XML documentation file, so that an unused `using` fails the build.**
IDE0005 is reported on build only when `GenerateDocumentationFile` is true; with it off the
compiler refuses the rule outright (`EnableGenerateDocumentationFile`). Keeping the file to the one
project OpenAPI reads would lose that gate everywhere else, so `Directory.Build.props` turns it on
for the whole repository with CS1591 and CS1573 suppressed: no member owes a comment, and a
`<see cref>` that is written still has to resolve.

**A success is serialised as the type the action declares, and its content type is left to the
result.** `Ok(value)` serialises the runtime type, and a `[JsonPolymorphic]` discriminator is
written only when serialisation starts at the base, so a closed hierarchy would go out without its
`status` tag; `ApiControllerBase` names the declared type on every success, and the idempotency
replay stores and writes the same one. No `[Produces("application/json")]` is set, because it
overwrites the `application/problem+json` every error response carries, and the base class
declares only the statuses that arrive ahead of any action — 413, 415, 429 and 500 — because an
action can add to the set `ProducesResponseType` builds but never remove from it.

**A refusal written before MVC carries its problem body whatever the request accepts.** The 413 of
the request-size limit, the 429 of the rate limiter and the 504 of the request timeout are written
by hand as `application/problem+json` rather than through `IProblemDetailsService`, which writes
nothing when `Accept` excludes JSON. Each carries the `code`, `traceId` and `type` every other
producer carries, and the timeout's matches `GlobalExceptionHandler`'s timeout arm, so a client
cannot tell which path answered.

**Reads may be stored only by their caller, and are revalidated before reuse.** A `GET` or `HEAD`
answers `Cache-Control: private, no-cache`, an action marked `[NoStore]` answers `no-store` whatever
its method, and every response varies on `Authorization` and `Origin`: each read is scoped to the
bearer and CORS is active, so a cache keyed on the URL alone could serve one caller's or one origin's
response to another. A `Cache-Control` or `Vary` already set by an endpoint is never overwritten.

**Rate limiting partitions on the client address, never on the user.**
<!--#if (!publicApi)-->
The limiter runs before authentication, so the user is still anonymous when it decides. Moving
authentication first would buy a per-user key at the price of validating the bearer — a signature
check, and with local accounts a security-stamp read — on every request the limiter was about to
reject, amplifying the load it exists to shed.
<!--#endif-->
<!--#if (localAuth)-->
The authentication policy would stay per address even with an identity at hand: it slows credential
discovery on endpoints where, by construction, there is none yet.
<!--#endif-->
Callers sharing an address share a budget, and behind a proxy the address is only right once
`ReverseProxy` names it.

**The request language is parsed by hand, and believed.** `UseRequestLocalization` is not used: it
negotiates against a list of supported cultures this template deliberately does not keep, and works
in `CultureInfo`, which `InvariantGlobalization` empties of meaning. `Accept-Language` is parsed by
hand rather than with `StringWithQualityHeaderValue`, which resynchronises inside a malformed entry
and hands back a fragment of it (`"not a tag"` yields `"tag"`); a malformed entry is skipped whole.
The winning tag is checked for shape only: a language nothing was written in falls back inside the
renderer that knows which languages exist.

<!--#if (localAuth)-->
**The token endpoints are never `[Idempotent]`.** Replaying a sign-in would mean keeping the issued
token pair in the idempotency store, a credential at rest in a table built to be read back.
<!--#endif-->
**An idempotency key needs a caller, and a started response keeps its claim.** A key sent on a
request whose caller cannot be identified is refused with `idempotency.callerNotIdentifiable`
rather than served unprotected, because keys are scoped per user and an unidentified caller would
otherwise lose the guarantee without being told. Once the response has started the claim is kept
even if the request then fails, since the write has committed and releasing the claim would let a
retry run it again; a failure between the commit and the first byte releases a claim whose write
landed, and the lease bounds that mistake.

**`AppTemplate.Presentation.Core` carries no database driver either.** `Npgsql.OpenTelemetry` would
bring the PostgreSQL driver into every host composing the project, a desktop or CLI host included,
so `AddObservability` takes `tracing` and `metrics` callbacks and each host adds its own
`AddNpgsql()`; `AppTemplate.Api` references the package itself. The ASP.NET packages it leaves out
are [`ARCHITECTURE.md`](ARCHITECTURE.md)'s subject.

**Each presentation project grants its internals to its own unit tests and to nothing else.**
`EntityTagMapping`, `IfMatchPrecondition` and `ProblemDetailsNormaliser.Normalise` are internal so
that nothing outside the API reads or mints a version token or skips the one normaliser every
problem passes through; the options validators, registration extensions, idempotency filter and
request-log middleware are internal because the container or the pipeline reaches them, never a
caller. Their branches are decisions a public-surface test could reach only through a live HTTP
pipeline, so `AppTemplate.Api` and `AppTemplate.Presentation.Core` each declare one
`InternalsVisibleTo`, naming their own unit-test project.
