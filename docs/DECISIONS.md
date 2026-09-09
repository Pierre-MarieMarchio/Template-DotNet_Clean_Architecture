# Decisions already made, and the shape they impose

These are the choices a reasonable person could have made differently. They are written here, and
where a test can hold one it does — a rule nothing verifies is re-derived and lost inside six
months. If one of them is wrong for your project, change it deliberately: change the sentence here
*and* the test that holds it, in the same commit.

Two neighbours. [`ARCHITECTURE.md`](ARCHITECTURE.md) argues the layer boundaries and lists what is
deliberately absent, and the template repository's own plans of record hold the measurements and
the rejected options behind each of these — see [`README.md`](README.md#plans-of-record).

**Writes are named operations. There is no `PATCH`.** A partial update whose semantics depend on
which keys the client happened to send cannot be validated against an invariant, because the
invariant is a property of the whole aggregate. So an omitted field means *absent*, not *unchanged*,
and every write says in its URL what it does. `NoEndpoint_AcceptsPatch` holds it.

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
consistent but stale. A counter makes the divergence observable
(`apptemplate.reminders.missed_cancellations`, watched per [`SECURITY.md`](../SECURITY.md)). **Any consumer you add
has to have that shape**, or the missing outbox stops being a cost and becomes a bug.

**The refusal of an outbox was re-examined against a feature that could have overturned it, and it
holds.** The `Files` feature looked like the counter-example: a thumbnail that is never generated
because an event went missing is visible, and bytes that are never reclaimed accumulate. It is not
one. The reclamation is a periodic sweep that re-derives its own precondition — an object no row
references is garbage — so the deletion event is a fast path that shortens an interval and nothing
more, in exactly the shape `CancelRemindersOnTodoItemCompletedConsumer` already had. Derivative
generation, when a project adds it, gets the same treatment: sweep for available files without a
derivative, and let the event only make it prompt.
The thing that nearly cost this its meaning was not the design. Three files in that feature
described the deletion consumer's behaviour in detail before the consumer existed, and every test
was green, because nothing related the events raised to the consumers registered.
`DomainEventTests` now does: every event is either consumed or written into a list saying why it is
not.

**Extract what two real cases prove identical, and nothing more.** A guessed abstraction is a worse
defect than an assumed duplication: the duplication is visible and local, the wrong abstraction is
neither. Measure first — `wc -l`, `diff` — and require two cases that do the same thing, not two
that resemble each other. `AggregateTracker` is what that looked like when it succeeded: seven
members that read nothing but what `IVersioned`, `IAuditable` and `AggregateRoot<Guid>` already
name. `FlushTo` stayed abstract, and the two repositories were never touched.

**Authorisation is default-deny.** The fallback policy requires an authenticated user, so a new
endpoint is closed until someone opens it. `[AllowAnonymous]` is the visible exception and it is
whitelisted by name in `DefaultDenyTests`; adding one is a line in that test.

**Four words name the four ways this template reaches storage, and each one is checked.**
`Repository` loads an aggregate, so its contract lives in `AppTemplate.Domain` beside the aggregate.
`Queries` projects rows onto a DTO without materialising one. `Store` is an application port for
storage with no aggregate behind it — `IIdempotencyStore`, which a use case depends on. `Table` is
row access to one table, declared inside the persistence project and reached only by a sibling
infrastructure module — `IRefreshTokenTable`, which no use case has ever heard of.
`StorageVocabularyTests` holds all four: a `Store` or a `Table` naming a domain entity has become a
`Repository`, a `Repository` declared outside the Domain is a promise it cannot keep, and a `Table`
a use case depends on has become a port and needs declaring where ports are.
The four-operation ceiling `PortConventionTests` enforces is a rule about **ports** — the façade a
use case sees. A `Table` is not one, and `IRefreshTokenTable` has six operations deliberately: it is
one table's whole surface, held narrow by having exactly one consumer rather than by a count.

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
`ILeaderLease` is an application port, and `FireDueRemindersUseCase` is what takes it — not
`ReminderBackgroundService`. A `BackgroundService` is a trigger; a guard placed there protects the
timer's callers and nobody else, and the two purges are already exposed over HTTP by
`MaintenanceController` as the standing reminder that a second caller does turn up. The adapter is
a PostgreSQL session-level advisory lock (`Common/Leases/`), chosen because losing the process
releases the lock rather than stranding a lease until a timer says otherwise. **It is not a fencing
token** — leadership can be lost mid-run — so anything run under it still has to survive a second
host starting it. `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` is
what holds the first sentence: a port this layer declares and never calls is a decision that has
left the layer, and the fix is to move the decision back, not to move the file somewhere the rule
does not look.

**No MediatR, no dispatcher, no pipeline behaviours.** A controller names the use case it calls, and
`F12` reaches the implementation. `LayerDependencyTests` forbids the package by name.

**A port never exposes `IQueryable`.** A contract that hands out a query tree has not abstracted the
database, it has published it — and every caller becomes a place where a lazy load can happen.
`NoApplicationPort_ExposesAQueryable` holds it.

