# What a derived project writes itself — plan of record

**Status:** closed, 2026-09-09. Wave E implemented D1 to D4 and withdrew D5; wave G is done, having
been released by the owner after the forks were closed. Every fork is closed — four of them
refusals, and the first is a refusal that started as this document's strongest recommendation.
Nothing here is left open.

Taken after `docs/plans/AUTH-SEPARATION.md`, which was itself taken after wave 7 of
`docs/plans/SDK-SPLIT-PLAN.md`. This document is self-contained: every count in it was measured on
2026-09-09 against the tree as it stands, not carried over from an earlier document, and where a
claim is a judgement rather than a measurement it says so.

This directory is exempt from `Tools/CheckDocPaths.cs`, so paths below may name a tree that does
not exist yet.

## The goal

The split gave the template six package-grade projects and a boundary a derived project cannot
cross by accident. What it did not do is reduce what a derived project **types**. This chantier
asks one question of the tree: of what a derived project writes for its first feature, how much is
its own, and how much is a shape this repository already holds three copies of?

Two halves, and only the first is scheduled:

- **Wave E** resorbs the duplication that is already measured and already agnostic, into the
  projects that exist for exactly that. It is the half with no product decision in it.
- **Waves F and G** are ergonomics rather than code: what the template hands over at generation
  time, and whether generating a feature is a task rather than a 645-line document.

The limits `docs/ARCHITECTURE.md` states are re-read here too — not to relax them, but because the
premise of three of them changed when the infrastructure layer got its agnostic half. That section
ends in two forks and neither is picked.

**Vocabulary.** As everywhere in these documents, "SDK" means the discipline of the six
package-grade projects, not a distribution — decision 38 of `docs/plans/SDK-SPLIT-DECISIONS.md`.

## What is measured — 2026-09-09, and it grew rather than shrank

`docs/plans/SDK-SPLIT-HANDOFF.md` records roughly 126 lines of "measured, agnostic duplication —
offered to the owner, not yet scheduled". That table was written before wave 6 and before the
separation of authentication. Re-measured today, every row still holds, and two entries belong on
it that were not:

| Where | What stands duplicated | Measured |
|---|---|---|
| The three aggregates | The `IAuditable`/`IVersioned` block: five property declarations and three explicit implementations | The three method blocks are **byte-identical**. The five properties are identical too; `TodoList` alone carries a five-line remark on `Version`. **22 lines × 3** |
| The three application services | `LoadOwnedAsync` — resolve the caller, load, refuse a non-owner as absent, check the precondition | 42, 42 and 45 lines. `diff` between the two closest is **100 % type substitution**: the aggregate, its repository, its not-found error, the parameter name. Not one line of differing logic |
| Two port projects | `TodoListPageRequest` and `StoredFilePageRequest` | 28 lines each, pure substitution but for one word of prose |
| Two query use cases | `GetTodoListsRequestBinder` and `GetStoredFilesRequestBinder` | 57 and 55 lines, and the `diff` is names only. The first one's own doc comment concedes the copy: *"the next paginated collection this feature grows will need its own copy of exactly this shape"* |
| Two filters | The optional-search-term block | **13 lines, strictly identical**, one type substitution apart |
| One filter | The ISO 8601 bound-parse block | **Twice, and both in `TodoListFilter`.** This is the weakest row and it is kept honest below |

**One claim to correct before it is repeated.** The bound-parse block is not a cross-feature
duplication: the other `DateTimeOffset.TryParse` sites in the tree are `CursorKeys.Validate` and the
two persistence sort maps, which parse a cursor key and a stored key rather than a caller's filter
bound. Two occurrences inside one file is under the bar `CONTRIBUTING.md` sets for an extraction
across features, and D5 says what is done about it anyway and why that is a different argument.

**What the whole of it comes to.** Around 170 lines of substitution, against roughly 126 in the
handoff's table, because the ownership gate (decision 27, never scheduled) and the search-term block
were not counted there.

**And the number that matters is not that one.** The simplest feature this template ships —
`Reminders`, no child entity — is **47 source files and 24 test files**: 4 domain, 28 application,
9 persistence, 6 API. Every row above is something the 48th file of a derived project's first
feature retypes.

## Decisions

### D1. `AuditableAggregateRoot<TId>` in `Domain.Core`

**Decided.** An abstract `AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditable, IVersioned`
in `Src/Domain/AppTemplate.Domain.Core/Common/Primitives/`, holding the five properties and the
three explicit implementations. The three aggregates derive from it and lose the block.

**Reason.** The block is byte-identical three times over, it is the shape every aggregate in a
derived project will need on the day it wants audit columns or optimistic concurrency, and both
interfaces it implements are already in this project. The word `Primitives` is already in
`LayoutConventionTests`' vocabulary for it, so no folder is invented.

**Why the setters stay explicit.** `IVersioned`'s own summary states the property this shape has:
the value is owned by the store, so a getter is public and the setter is reachable only through a
cast that is a visible act. Moving the implementations into a base class keeps that exactly — it
does not widen anything, it stops three files restating it.

**What does not move.** `TodoList`'s five-line remark on `Version` says why the token lives on the
root and not on the item, which is a fact about that aggregate's consistency boundary rather than
about the base class. It stays on the derived type or goes to the base's own summary in the general
form; it is not deleted.

**Rejected — leaving it.** It is the row on the handoff's table with the highest count and the
lowest argument against it.

**Rejected — a second base class for aggregates that want auditing without versioning.** No
aggregate in this template wants one without the other, and a pair of base classes for a
distinction nothing exhibits is shape without substance — the same objection A1 of
`docs/plans/AUTH-SEPARATION.md` raised against a `Domain.Auth`.

**Three things the move measured that this entry did not predict.**

- **A `<see cref>` cannot bind an inherited member through a constructed generic base.**
  `<see cref="TodoList.Version"/>` stops resolving the moment `Version` is declared on
  `AuditableAggregateRoot<TId>`, and `CS1574` is an error here. Four sites, in `StoredFile` and in
  `TodoListDtoMapping`; each now names the interface that owns the member — `IAuditable.CreatedAt`,
  `IVersioned.Version` — which is what the surrounding sentence was already saying.
- **A private setter declared on a base is invisible from the derived type**, so
  `PropertyInfo.SetMethod` reads as null there. That re-expressed one domain test's assertion, and
  it did something worse to one rule: **`DomainModelTests.Entities_HaveNoPubliclySettableState`
  would have gone quietly vacuous over all five stamps** — the accessor reads as absent, so the loop
  `continue`s rather than checks. The rule now walks each entity's inheritance chain with
  `DeclaredOnly`, which checks strictly more than it did before, and its offence list is
  de-duplicated because one base's offence is otherwise reported once per entity inheriting it.
  **Proved it can still fail** rather than assumed: making the base's `CreatedBy` setter public
  fails it, and the message names the base rather than the aggregate.
- **The `PublicAPI` analyser catches that sabotage first**, before the architecture rule sees it: a
  new public accessor is `RS0016` at build time. Two guards over one property, and the outer one is
  the faster of the two.

### D2. The ownership gate goes inward, which closes decision 27

**Decided.** `IOwnedAggregate` in `Domain.Core/Common/Abstractions/` (an aggregate that holds a
`UserId OwnerId`), and the gate itself in `AppTemplate.Application.Core`. The three feature services
keep their named interface, their not-found error and their registration, and their body becomes a
call.

**Reason, and it is decision 27's own conclusion rather than a new one.** That entry was withdrawn
because the extraction does not populate the *business* `Common/` it was proposed for — but it
established, and measured, that the extraction is sound and that decision 17's sorting test sends it
one project inwards. It then said, in as many words, that what remained was a question for the
owner. This is that question answered.

**What stays in the feature, and it is the load-bearing half.** The policy that a non-owner is
answered exactly as an absent resource is expressed by each feature passing **its own** not-found
error. That is why the gate takes the error rather than minting one: the day a feature wants to
answer a non-owner differently, it changes one argument instead of escaping an abstraction.

**The shape, and why not the shortest one.** The gate takes the loaded aggregate, the caller's id,
the not-found error and the optional precondition, and answers a `Result<TAggregate>`. Each service
keeps its `RequireUserId` and its `repository.GetAsync` in plain sight.

**Rejected — a gate that also loads,** taking a repository abstraction or a loader delegate. It
collapses each service to a single line and it costs either a generic repository port the domain
does not have — repositories are per-aggregate interfaces declared in `AppTemplate.Domain`, on
purpose — or a `Func` parameter that makes the call site less readable than the four lines it
replaces.

**Rejected — a base class for the three services.** They are registered per feature and each has its
own named interface; inheritance would put a container-resolved type in a base class for the sake of
one method.

**A new word in a closed vocabulary, and that is deliberate.** `Application.Core/Common/` gains one
entry for this subject. `LayoutConventionTests`' dictionaries are closed precisely so that inventing
a word is a decision a reviewer sees, and this is one.

**What D2 measured.** The three services go from 42, 42 and 45 lines to 32, 32 and 34, and the
duplicated body — resolve, load, refuse, compare — stands once. Each service keeps `RequireUserId`
and its `GetAsync` in plain sight, and hands the gate its own error.

**`IStoredFileService`'s own paragraph had to be rewritten, because the extraction falsified it.**
It argued that a generic gate "would have to be handed all three — at which point the caller is
writing the gate again with extra ceremony". Measured, the caller hands it two: the loaded aggregate
and the error. The repository and the caller's identity stay where they were. What the paragraph now
says is the true half of what it was reaching for — the three share the mechanism and not the
policy.

**One full-solution run in four failed, and it is recorded rather than explained.** 326 tests, the
whole of `AppTemplate.Api.IntegrationTests`, with the run lasting 9.9 s against the 51-53 s the same
suite takes when its containers start. No assertion was involved and no container was left behind.
Three runs before and after it are green at 3350. This is the shape decision 31 describes — twelve
test projects and four containers starting at once — and it is **not** diagnosed here.

### D3. `FeaturePageRequest<TFilter>` in `Application.Core/Common/Collections/`

**Decided.** One generic record replacing `TodoListPageRequest` and `StoredFilePageRequest`, holding
the paging, the sort and the feature's own filter.

**Reason.** The two are the same 28 lines with one type substituted, and what they exist to say —
*this shape is unconstructible without having gone through the whitelist* — is a property of the
mechanism, not of a feature. `Collections` is already the word.

**The one thing to preserve.** Both types keep their factory `internal` so nothing outside the
project that owns them can assemble an unvalidated request. Generic and public, the factory has to
stay reachable only from the binder that validates — so the type is public and its construction is
not, which is the same arrangement one project outwards.

**Rejected — a non-generic base record with the filter as `object`.** It removes the compiler check
that a feature's queries port receives that feature's filter, which is the whole of what these two
types buy.

### D4. The binder becomes generic over the filter, and the query records are not touched

**Decided.** The order-parse, paging-resolve and assembly steps move into `Application.Core`; each
feature's binder keeps what only it knows — its policy, its tiebreaker field, and the call that
builds its own filter.

**Rejected — a shared base record for the query types.** The five leading members of
`GetTodoListsQuery` and `GetStoredFilesQuery` are identical and a base record would factor them.
They are also **the HTTP contract**: they are model-bound from the query string and their `<param>`
documentation is what the OpenAPI document publishes as each parameter's description.
`OpenApiDocumentTests` asserts on exactly that, and decision 28 of
`docs/plans/SDK-SPLIT-DECISIONS.md` is the entry recording how quietly those descriptions can be
lost. Saving ten lines is not worth putting the generator's input through an inheritance it has
never been asked to carry here.

**Consequence accepted.** The generic binder takes the five raw values rather than a query object,
so the call site names them. That is more typing at one call site per collection, and it keeps the
contract types exactly as they are.

### D5. Withdrawn — `Result` cannot carry "none", and that is deliberate

**This entry claimed** that `SearchTerm` should gain a factory for the optional case, so the
13-line block would leave both filters. **It is withdrawn**, and what withdrew it is worth more than
the 26 lines were.

**`Result.Success` refuses a null value, by a guard whose own summary says why**: absence is a
failure carrying an `Error`, never a success holding null — written after an adapter returning null
under a non-nullable declaration produced a success a controller served as a 200 with an empty body.
So `Result<SearchTerm?>` **compiles and throws**: it is a shape this codebase does not have, on
purpose.

**Found by the tests rather than by reading**, which is the part to keep: the build was clean and
**103 tests failed** with `A successful result must carry a value`. This was the first of the three
items the entry below lists as unverified, and the answer is the one that closes the entry.

**What was rejected on the way out.** Relaxing the guard — it exists because of a real defect.
An option type — inventing one for two call sites is the guessed abstraction `CONTRIBUTING.md`
forbids. A continuation shape, `Search<TFilter>(value, build)` — it works, and it turns a readable
13-line block into a nested lambda that the to-do list filter would then parse two dates inside of.

**What is left for the owner, and it is a real option rather than a formality.** A nullable
`Result<SearchTerm>?` — null for "the caller sent nothing", a failed result for "refused", a
successful one for the term — needs no new type and bends no invariant, and takes the block from 13
lines to 7. It is not taken here because it gives this codebase a second way to say "absent" one
paragraph away from a guard insisting there is only one, and that is a judgement about the
vocabulary rather than about the lines.

**Nothing else in wave E depended on it.** The three edits were reverted and the tree is back to the
block that works.

**The bound parser goes with it, and was never started.** Its two occurrences are in one file,
which is under the bar `CONTRIBUTING.md` sets, and it was already the entry's own weakest row. It is
a question for the owner beside the one above rather than a thing wave E does.

### D6. The limits are re-read, three have a changed premise, and none is relaxed here

The capabilities `docs/ARCHITECTURE.md` lists under `What is deliberately absent` were each written
against a tree that no longer exists in two respects: the infrastructure layer now has an agnostic,
package-grade half, and authentication owns its own context. Re-read against today's tree:

| Limit | Verdict |
|---|---|
| **An outbox** | **The premise changed and the answer is still no — see the closure below, which corrects this entry's own claim.** The four mechanisms an outbox is made of do all exist now. What does not exist is a consumer that needs one. **Closed** |
| **Tenants** | **Decided: no.** It is still the only absent capability whose retrofit cost grows with every feature written — a column on every owned table, in every index, and in every ownership check. The closure below says what would reopen it. **Closed** |
| **Machine-to-machine authentication** | Cheaper than it was — the auth module owns its storage, so a key table has an obvious home, and `IAccessTokenIssuer` is the port. Still a feature with a security surface, and no caller in this template needs one. Unchanged |
| **A business audit log** | Cheaper too, for D6's first reason: an history interceptor would now be a package-grade mechanism any second context inherits. No consumer. Unchanged |
| **A distributed cache** | **No code change is available to make.** `HybridCache` takes a second level through configuration alone, which decision 35 states and the absent-capabilities table does not. One sentence of documentation, in wave E |
| Permissions and policies | Unchanged. `IAuthorizationPolicyProvider` is the extension point and nothing about it moved |
| A message bus or queue port | Unchanged. `PeriodicJob` plus `ILeaderLease` is the shipped answer and it is coherent |
| Minimal APIs | Unchanged, and for the reason that made it a limit: idempotency is an action filter, `ApiControllerBase` maps results, ETags hang off the same machinery. Flipping it costs what it always cost |
| Output caching | Unchanged. Every read is a per-caller one behind default-deny |
| A client contracts package | Unchanged. One OpenAPI document per version is the route, and a shared binary undercuts the versioning |
| A reusable test kit | Unchanged, and decision 36's measurement was re-read rather than re-taken: 356 agnostic lines against `ApiFactory` and `IntegrationTestBase`, which name ten and eight product namespaces. Generation copies all of `Tests/`, which is the delivery mechanism |
| `Api.Auth` | Unchanged. Closed twice, the second time with a bench — A7. It reopens the day the module leaves the process, and the two expensive measurements are already recorded there |
| Soft delete, `AutoMapper`, a service layer, an abstraction over `DbContext`, Swashbuckle, a refresh cookie, migrations at startup | Design choices, no premise moved |
| The shared due date, deferred by decision 32 | It resorbs no duplication, which is why it was deferred, and the worked example of a business `Common/` it would have been a second of now exists. Leave deferred |

**Neither fork is picked here.** The owner has been shown both.

## Waves

Sequential, each green on the full exit gate of `docs/plans/SDK-SPLIT-PLAN.md`.

### Wave E — resorb what is measured. **Done, 2026-09-09.**

D1 to D4 landed; D5 was written, measured against `Result`'s own guard, and withdrawn. Every step
was a deletion plus a public surface entry, and none of them changed behaviour — the tests that
existed were the oracle throughout, and the two places one had to change its assertion rather than
its `using` are each recorded above with why the guarantee did not move.

**Green on the full gate.** `dotnet build` 0 errors and 0 warnings; **3350 tests**, none failing,
none skipped, with Docker present so both integration suites contributed; **131 architecture rules**;
all six packable projects pack; both container images build; every hygiene gate and
`dotnet format --verify-no-changes` clean.

**What it removed, measured rather than estimated.**

| Subject | Before | After |
|---|---|---|
| The audit and version stamps | 22 lines in each of three aggregates | one base class, and an aggregate declares nothing |
| `LoadOwnedAsync` | 42, 42 and 45 lines | 32, 32 and 34, the body standing once |
| The feature page request | two 28-line records | one generic record, unconstructible outside the project that validates |
| The request binder | 57 and 55 lines | 33 and 33, and what is left is what only the feature knows |

**Two rules check more than they did**, and neither change was foreseen: the domain-model setter and
field rules now walk the inheritance chain, and `CollectionContractTests`' floor of six becomes eight
with the enumeration it had been carrying incomplete since before this chantier.

**What the blast radius asked for, and it is all done.** `CONTRIBUTING.md`'s canonical tree carries
`Common/Ownership/`; `docs/ARCHITECTURE.md`'s aggregate section names the auditable base and its two
absent-capability rows are corrected; `docs/ADDING-A-FEATURE.md`'s steps 1, 2 and the collection
section describe the tree that now exists, and it gains section 5b — the closed lists a new feature
lands in, which is the knowledge half of a scaffolder; `CHANGELOG.md` has one entry per subject.

**`coverage.minimum` is re-measured, and 90 stays.** With Docker present and the `--` separator:
**94.69%** in Debug (7269/7677) and **95.92%** in Release (6036/6293), over 3350 tests. The Debug
total fell 0.07 points, and the reason is worth recording rather than absorbing: **a shared generic
record brings compiler-generated equality members no test calls**, so resorbing two duplicated types
into one moves a handful of measurable lines from covered to not while nothing stops being
exercised. Two of fifteen assemblies still sit under the floor, the same two.

### Wave F — `--no-examples` at generation time. **Offered, not scheduled.**

`.template.config/template.json` exposes `name` and its derived symbols and nothing else, while
`docs/REMOVING-THE-EXAMPLE-FEATURES.md` is a 689-line manual procedure.

**The premise for this one changed too.** Decision 14 predicted that removal would become "deleting
a folder and one call" once registration was opt-in per feature, and wave C then removed the
`IIdentitySeeder` coupling. The conditional-source route is cheap now in a way it was not.

Two things it has to state rather than hide:

- **`--no-auth` alone is not coherent** and must not be offered as though it were.
  `IReminderNotifier` still holds the container, deliberately — A6 keeps that door. But that
  coupling is between two *example* features, so `--no-examples --no-auth` is coherent where
  `--no-auth` is not. Plus the regenerated migrations A5 describes.
- **A generated project without the examples has a weaker rule suite.** Several fitness rules use
  the example features as their sensitivity probe — decision 14 names them. That is a fact to write
  into the generated project's own documentation, not one to discover.

### Wave G — a feature scaffolder. **Done, 2026-09-09**, released by the owner.

`dotnet run Tools/Tasks.cs new-feature Widgets Widget` writes **26 files** across the four layers.
What makes it defensible here rather than in a general project: the layout is rule-enforced,
namespace equals project plus folder path over some 600 files, and 131 architecture rules already
run over the tree — the oracle for its output already exists.

**Three decisions shape it, and each is the same one this repository keeps making.**

- **It creates files and edits none.** A vertical does not compose until its registrations are
  written by hand, and that is deliberate everywhere else: registration is opt-in per feature so a
  feature nobody composes is visibly registered nowhere. A generator reaching into a composition
  root would take that property away, so it prints the five edits instead.
- **It invents no business logic.** The aggregate has an owner and the two factories every
  aggregate here has; it has no property nobody asked for. What it saves is the typing.
- **The product prefix is read off the tree.** A tool that hard-coded `AppTemplate` would work only
  in the repository it was written in, which is the one place it is least needed.

**Proved end to end rather than asserted.** The vertical was generated into this tree and built:
eight compile errors, all in persistence, all tracing to the two `AppDbContext` members the tool's
own report names. Adding those two, the build is **0 errors and 0 warnings** — which validated the
guesses that could have been wrong: `OwnedAggregate.Require`, `Result.Success()`, `result.Map`,
`IUseCase<Result<Guid>>`, `OkOrProblem`, `CreatedOrProblem`, `NoContentOrProblem`, `[Idempotent]`.
It found one real template defect on the way: `CreatedOrProblem` has an overload taking a plain
object, so an untyped lambda is ambiguous. Then the whole thing was regenerated from the corrected
template and built clean, and the trial was reverted.

**What the run measured about the closed lists, and it corrected this document's own guide.** With
the context wired and nothing else, **four** of 131 rules are red, and all four are composition. The
layout and vocabulary rules stay green, because a feature using the shape's own words is what they
are checking for. Two rows of `docs/ADDING-A-FEATURE.md` section 5b were wrong as written:
`_expectedEntities` and `FeatureFolderVocabularyTests` both assert that what is known to be there is
still there, so neither is touched by a new aggregate or a new feature folder. The section says so
now, and says it is the opposite of the obvious guess.

**Its `--self-test` runs with the other hygiene gates**, which is the idiom every `Tools/` file here
follows: it generates into a throwaway tree and checks what this tool is responsible for — a BOM on
every file, LF endings, a namespace that is the project plus the folder path, a declared type named
exactly like its file, no token left unsubstituted, and a refusal to scaffold twice.

## Blast radius

| Item | Work |
|---|---|
| `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` | `Domain.Core` and `Application.Core` both gain entries. Regenerate mechanically from `RS0016`, the way `docs/plans/SDK-SPLIT-HANDOFF.md` prescribes — never by hand. `RS0017` is the direction that catches a removal |
| `LayoutConventionTests` | One new word in `Application.Core`'s `Common/` vocabulary (D2), in both directions |
| `PackageBoundaryTests` | Every type added is agnostic by construction; the rule is what proves it, and a failure here means the extraction named a business type |
| `DomainModelTests` | Aggregate roots must stay sealed; the new base is abstract. Re-read the rules that walk the domain model for anything assuming `AggregateRoot<TId>` is the direct base |
| `CollectionContractTests`, `UseCaseConventionTests`, `PortConventionTests` | Populations and anti-vacuity floors: D3 and D4 move types between assemblies, which is exactly the shape that makes a rule go quietly vacuous |
| Unit tests | The three service tests, the two binder tests, the filter tests and the aggregate tests all keep their subject. A test that has to change its *assertion* rather than its `using` is the signal that the extraction changed behaviour |
| `coverage.minimum` | Re-measure at the end, as the file's own rule prescribes, with Docker present and with the `--` separator |
| `CHANGELOG.md` | One entry per subject, per decision 41 — what changes for a generated project, not what the work was called |
| `docs/` | `ARCHITECTURE.md` (the primitives, the ports table, the absent-capabilities note), `ADDING-A-FEATURE.md` (steps 1 and 2 shrink), `CONTRIBUTING.md`'s canonical tree if a folder is added |
| `docs/plans/SDK-SPLIT-HANDOFF.md` | Its table of five plan documents gains a sixth row, and its "offered, not yet scheduled" section points here |

## What is verified, and what is not

**Verified by measurement**, 2026-09-09: every count in the table at the top, the `diff`s behind
each "pure substitution" claim, the 47-and-24 file count of the `Reminders` vertical, that
`Application.Core` still registers nothing (so decision 16 holds unchanged), and that
`.template.config/template.json` exposes no symbol beyond `name`.

**Judgement, not measurement:** D2's shape (the gate does not load), D4's refusal of a shared query
base record — the risk it names is real and recorded in decision 28, but that this particular
inheritance would trigger it is a judgement — and D5's bound parser, which says so itself.

**Both open questions this document listed were answered by implementing it.** `Result<T>` accepts
a nullable type argument at compile time and refuses it at run time, which is what withdrew D5. And
no rule assumed `AggregateRoot<TId>` was an immediate base — `TypeFacts.DerivesFromOpenGeneric`
already walked the chain — but one rule did read *properties* off the entity alone, which D1
records.

## The forks, closed — 2026-09-09

All five were released to the implementer. Four are refusals, and the first is a refusal that
started as this document's strongest recommendation.

### 1. No outbox, and this document's own claim for it was wrong twice over

**The claim was that `FireDueReminders` is a consumer that needs one** — mail sent in process, at
most once. Read against the code, both halves are false. That use case's own summary says
**"Delivery is at-least-once"**: it claims a reminder, notifies, marks it notified and commits the
batch, so a crash between the notification and the commit re-fires it. And it is not a
domain-event consumer at all — it is a claim-based batch under `ILeaderLease`, which is the shape an
outbox *drain* has, not the shape of something an outbox would serve.

**What the real population says is decisive, and it is a design rather than an omission.** Domain
events have exactly three consumers, and each is deliberately not load-bearing:

| Consumer | Why an outbox buys it nothing |
|---|---|
| `ReclaimContentOnStoredFileDeletedConsumer` | Its own summary: *"This is a fast path, not the correctness guarantee. Deleting this class would not leak a single byte."* `ReclaimOrphanedContentUseCase` lists the store, subtracts the keys live rows name, and removes the difference — and covers a case no event could, bytes deposited against a grant whose registration was swept before it was confirmed |
| `CancelRemindersOnTodoItemCompletedConsumer` | `FireDueRemindersUseCase` re-checks completion itself, and says why: *"Removing it makes that consumer load-bearing."* The firing pass is correct whether or not that delivery happened |
| `LogTodoItemCompletedConsumer` | It logs, and calls itself a worked example |

So the template already has something stronger than at-least-once delivery, and it is stated as a
rule the consumers are held to: **an effect re-derives its own precondition.** An outbox would give
reliable delivery to three consumers designed not to need it, add a table, a migration, a fourth
interceptor, a drain loop and poison handling — and it would quietly make that discipline look
optional, which is the part that costs something. The extension point stays where
`docs/ARCHITECTURE.md` puts it, and that row now carries the positive half of the argument rather
than only the absence.

**The lesson is the one decisions 7, 27, 30 and 35 each recorded**, and this document earned it in
its own first pass: a claim that names a consumer must be checked against what that consumer
actually is, before anything is designed around it.

### 2. No tenants

The reasoning in the table stands and the cost is real, so it is worth saying what would reopen it
rather than only refusing: **a second owner-shaped column arriving in the model.** The day anything
is scoped by something other than `UserId` — an organisation, a workspace, a customer — that is the
moment, because it is the last one at which the ownership gate `OwnedAggregate.Require` is a single
place to change. After that the cost is per feature, for ever.

### 3. Wave F is not done, and the measurement that killed it is the useful part

This document claimed the conditional-source route was "cheap now in a way it was not". **Measured,
it is not.** Outside their own folders, the example features are named in **68 files**, including
**12 of the 44 files** of the architecture suite, both integration fixtures (`ApiFactory`,
`IntegrationTestBase`), the initial migration and its snapshot — and `Common/Tagging/`, which *is*
decision 32's worked example of the business `Common/` and is built on two of the examples.

So `--no-examples` does not remove illustrations. It removes the fitness suite's sensitivity probes
and the only worked example of one of the two kinds of `Common/`, and it does so **silently** —
which is strictly worse than `docs/REMOVING-THE-EXAMPLE-FEATURES.md`, whose 689 lines at least tell
a reader what they are giving up. Decision 14 said the examples were load-bearing for the rules;
this is the measurement of how load-bearing.

**What the same measurement says about the real cost**, and it points the other way: a derived
project keeps the examples and **adds** features beside them. The recurring cost is addition, not
removal — which is wave G's subject, not wave F's.

### 4. Wave G stays offered, and is now scoped by what wave E learned

It was refused here as a chantier not among the forks released, and the owner then released it; the
wave G section above records what it became and what proving it measured. The judgement that stood
behind the refusal still holds and is worth keeping: **the mechanical typing is no longer where the
cost is.** D1 to D4 removed four of the shapes a new feature used to retype, and what remains is the
part a generator cannot write — the closed lists, in `docs/ADDING-A-FEATURE.md` section 5b. The
generator is worth having on top of that, not instead of it.

### 5. D5 stays withdrawn, bound parser included

The nullable-`Result` shape works and was not taken. In a repository whose purpose is to be read and
copied, a second way to say "absent" one paragraph from a guard insisting there is only one costs
more than the twelve lines it saves. The block stands as it is, in both filters.

### 6. Done rather than deferred

The absent-capabilities note about a configurable second cache level is written, and while that
table was open the outbox row gained the argument from closure 1.
