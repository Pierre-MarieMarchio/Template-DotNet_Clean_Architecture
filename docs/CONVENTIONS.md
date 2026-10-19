# Conventions

Naming, visibility, where a contract lives, and what a comment is allowed to say. Where the files
go is [`PROJECT-LAYOUT.md`](PROJECT-LAYOUT.md); the choices behind these conventions are
[`DECISIONS.md`](DECISIONS.md).

Most of what follows is enforced by a test in `Tests/Architecture/AppTemplate.Architecture.Tests`,
so you will find out anyway — but finding out from a red build is slower than reading it here.

- [Design conventions](#design-conventions)
- [Naming](#naming)
- [Comments](#comments)

## Design conventions

**Interfaces.** Every service class has one; code depends on the interface and DI supplies the
implementation. An interface with a single implementation is fine here — that is a deliberate
testability choice, not an oversight. Default visibility is `internal sealed`; only ports,
configuration-bound options classes, and types the host must name are `public`.

**Where a contract lives.** A repository contract goes in `AppTemplate.Domain`, beside the
aggregate it speaks in, because it speaks only in domain types; every other port belongs to the
application layer. Which of that layer's projects declares which port — one feature's, several
features', authentication's — and the naming rules below apply to all of them, are
[`ARCHITECTURE.md`](ARCHITECTURE.md#ports-are-named-for-business-intent-not-for-technology)'s
subject. `AdapterVisibilityTests` recognises a repository contract from a namespace ending in
`.Repositories`, so follow the folder name exactly.

**Use cases.** One class per use case, behind **one named interface** inheriting
`IUseCase<TRequest,TResponse>` (or `IUseCase<TResponse>`) — the named interface is what constrains the
signature and what the controller depends on, and the container **throws at startup** if a use case
declares zero or several of them.
[`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md#2-application--the-use-case-and-its-port) covers the
registration mechanics. Never write `if (x != null)` without an `else`.

**Failures.** An expected outcome is a `Result`/`Result<T>` carrying an `Error` with a stable
machine-readable `code`; only a bug is an exception —
[`ARCHITECTURE.md`](ARCHITECTURE.md#errors-result-for-expected-failures-exceptions-for-bugs) states
the full rule, including `DomainException` for a violated invariant and
`ConcurrencyConflictException` for a lost update. Everything becomes an RFC 7807 `ProblemDetails`
with that `code`, and **no exception message ever reaches a client**.

**Persistence.** EF maps persistence models (`*Record`), never the domain entities, and the
concurrency token is PostgreSQL's `xmin`. The mapper, the tracker and the flush pipeline that keep
that split intact are
[`ARCHITECTURE.md`](ARCHITECTURE.md#why-a-separate-row-type-at-all)'s subject. If you are
changing any of that, add tests for the *update* path, not just the insert path: an insert that
works proves nothing about a delta EF computed from a tracked row.

## Naming

**A type's name says what it is, not only what it is about.** Reading the file name alone has to
answer "what nature of thing is this?", so the suffix is not decoration:

| Suffix | What it names | Filed under |
|---|---|---|
| `…UseCase`, `…Command`, `…Query` | one operation, its input, and its implementation | `UseCases/{Commands,Queries}/<Operation>/` |
| `…Outcome` | the record an operation hands back — a use case's or a port's | beside what returns it: the operation's folder, or `Ports/<Port>/` |
| `…Status` | the closed enum of ways it went, carried by an `…Outcome` | beside that `…Outcome` |
| `…Repository`, `…Store`, `…Table`, `…Queries` | the four ways this template reaches storage — see [`DECISIONS.md`](DECISIONS.md) for which is which | the adapter in `Access/`; a repository contract in the domain's `Repositories/`, any other contract in `Ports/<Port>/` |
| `…Service` | an injected implementation that has dependencies | `Services/` in the application, `Adapters/Services/` in infrastructure |
| `…Mapper` / `…Mapping` | a mapping, injected / static — and named for what comes **out** of it | `Rows/Mapping/` / `Model/Mapping/`, or `Contracts/Mapping/` for the wire |
| `…Helpers`, `…Extensions` | a static class of pure functions, or of extension methods — within the bounds [below](#helpers-and-the-banned-words) | `Helpers/` in a feature; in a `Common/`, with the type it extends — `ResultExtensions` in `Common/Results/`, `CurrentUserExtensions` in `Common/Ports/`. In a host's `Common/` an `…Extensions` is the composition class holding that folder's `AddX`/`UseX` |
| `…BackgroundService`, `…Instruments` | a hosted loop and the counters and spans it emits | together, under the API's `Features/<F>/Jobs/` |
| `…Dto`, `…Validator` | a shape the application answers or is given, and the rules an input is checked against | `Model/Dtos/` — an operation's validator in that operation's folder |
| `…Options`, `…Policy`, `…Consumer`, `…Controller`, `…Request`, `…Response` | as they read | `Configuration/Options/`, `Model/Policies/`, `Services/Consumers/`, `Controllers/`, `Contracts/Requests/`, `Contracts/Responses/` |

The suffix is the first hop and the folder the second: the whole table, every concept and nature
folder, is [`PROJECT-LAYOUT.md`](PROJECT-LAYOUT.md#the-suffix-gives-the-folder)'s.

Two rules follow from having been broken:

- **A word names one notion in this repository.** `Outcome` once named both a use case's return
  record and the enum inside a port's, and `Policies` named both a business rule under
  `Features/<F>/Model/Policies/` and the file holding an `AddApiX`. When a second meaning appears, one of
  the two is wrong — find which and rename it, do not document the ambiguity. Two more that were:
  `Access` named what `Service` already named, and `Verdict` named what `Decision` already named —
  a policy's chosen action, as opposed to the `Status` an observation reports.
- **A port is a port at both scopes, and the word says so.**
  `AppTemplate.Application.Core/Common/Ports/` holds the ones every
  feature reaches for — the clock, the unit of work, the mail relay — and
  `AppTemplate.Application/Features/<F>/Ports/<Port>/`
  the ones one feature does. There is no `Abstractions/` in the application layer: every interface
  it declares is an abstraction, so the word sorted nothing, and it hid two interfaces the layer
  *implements* rather than consumes among the ones infrastructure satisfies. That reasoning is
  `AppTemplate.Application.Core`'s to keep, since `Common/` is its folder alone, and those two live
  with their subject there — `IUseCase` in `Common/UseCases/`, `IDomainEventConsumer` in
  `Common/Events/` — which is what let `PortConventionTests` drop the exclusion list it needed to
  tell them apart. `AppTemplate.Domain.Core` keeps a `Common/Abstractions/`: `IAuditable` and
  `IVersioned` are opt-in contracts a persistence row satisfies, not capabilities the domain calls
  out for.
- **The port carries the nature word, and the adapter is the port without its `I`.**
<!--#if (localAuth)-->
  `IAccountsService` is implemented by `AccountsService`; `ISecurityEventLog` by
  `SecurityEventLog`, because `Log` already says what it is.
<!--#else
  `IIdempotencyStore` is implemented by `IdempotencyStore`, because `Store` already says what it is.
#endif-->
  A qualifying prefix is right in two
  cases. The first is a port several modules implement, where the prefix tells them apart:
  `MailKitEmailSender` and `InMemoryEmailSender` both satisfy `IEmailSender`. The second is a
  single adapter whose technology is visible at the call site — `EfUnitOfWork`, because saving is
  the one place the choice of Entity Framework shows, and `PostgresLeaderLease`, because which
  store the lock is taken in is a property a caller has to reason about.
  A port's *folder* keeps the capability name — `Ports/<Capability>/I<Capability>Service.cs` — and
  that is not cosmetic: a folder named for the interface would put a namespace and a class of the
  same name in scope of each other, which is CS0118 at every consumer.

### `Helpers` and the banned words

Banned outright: `Manager`, `Utils`, `Processor`, a bare `Handler`, and `Composer`. Each of them
names "code" rather than a thing, and each attracts whatever nobody could classify. There is no
`Utils/` folder in this repository and there is not meant to be one; needing it means a name is
missing, not a folder.

`Helper` is the one such word allowed, on a mechanical bound instead of a ban:

- **A Helper is a static class of pure functions** — formatting, computation, parsing, pure
  conversions, convenience constants, extension methods — over domain types, Dtos or primitives.
  Business or not: purity is the criterion, not the subject.
- **It is never instantiated, never injected and never registered.** It holds no mutable state,
  implements no interface and performs no I/O, no database and no HTTP — a type with a dependency
  is a `Services/` collaborator in the application and an adapter in infrastructure.
- **It is not something with a home of its own.** A rule that protects a state is a
  `Model/Policies/` policy; a wire type belongs in `Contracts/`; the entity, value object or Dto
  itself in `Model/`. A file with a more precise nature — a `…Mapper`, a `…Policy`, a `…SortMap` —
  goes to that nature's folder: `Helpers/` is the last resort, never the first.
- **One public static class per file, suffixed `…Helpers` or `…Extensions`**, named for what it
  helps. `Helpers/` is the plural of that suffix, not a bucket, and the banned words above stay
  banned inside it.
- **There is no `Helpers/` in a `Common/`.** A helper of the shared half lives with its subject,
  as `ResultExtensions` lives in `Common/Results/`.

`FeatureFolderVocabularyTests` holds the bound: every type declared under a `Helpers/` folder is a
static class, which also bars it from implementing an interface.

## Comments

Minimal and short. A comment says **what** something does, or **how** it works, when the signature
does not. It never says why the thing has this shape rather than another. The test is: **if I delete
this comment, can someone introduce a bug?** If not, it goes.

A developer arriving on this repository has to be able to understand the architecture and the code
by navigating them. So:

- **No paraphrase of the code.**
- **No orchestration or construction commentary** — what was weighed, what was rejected, why this
  call sits here. That is noise between the next reader and the code.
- **No narration of the repository's own history.**
- **XML doc only when it says something the signature does not**, and not on every public member by
  reflex.

**Rationale lives in `docs/`.** Measurements, rejected options and arbitrations belong in
[`ARCHITECTURE.md`](ARCHITECTURE.md), in [`DECISIONS.md`](DECISIONS.md), or in a plan of record
under this directory — where a reader who wants them can address them directly. A design decision
worth keeping is worth a section there, not a paragraph above a method.

<!--#if (templateMaintenance)-->
<!-- narrative-ok: stating this rule requires quoting the phrases it bans -->
`Tools/CheckNarrativeComments.cs` executes the history half of that rule over every `.cs` and
`.md` file, `CHANGELOG.md` excepted — narrating history is what a changelog is for. Its pattern
list ("this used to…", "the old implementation…", "fixed the bug where…") is deliberately narrow,
because the same words are legitimate or banned depending on the tense they carry: "a v2 added
later would show up inside the v1" is design rationale, and no regular expression separates it from
a sentence about this repository's past. A line that cannot avoid the construction carries a
`narrative-ok: <reason>` marker, and the marker count is printed so exemptions cannot spread
unnoticed.
<!--#endif-->

