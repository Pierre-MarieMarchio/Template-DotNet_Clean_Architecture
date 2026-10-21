# Adding a feature

A worked walkthrough of the vertical [`../CONTRIBUTING.md`](../CONTRIBUTING.md#adding-a-feature)
summarises: aggregate → EF model → mapper → tracker → repository → use case (with its
named interface) → controller → tests → migration. `TodoLists` is the real example
running through every step below — open the file next to the paragraph describing
<!--#if (files)-->
it. `Reminders` is the same vertical without child entities, and `Files` is the one
whose state does not all live in the database; sections 4b and 4c say what each of
those changes.
<!--#else
it. `Reminders` is the same vertical without child entities; section 4c says what
that changes.
#endif-->
<!--#if (publicApi)-->

**This project was generated with `--auth none`, which adds no feature.** Every feature belongs to a
caller, and a public API has none: `dotnet run Tools/Tasks.cs new-feature` refuses it, and the
`--examples` and `--files` options that would have brought features are refused at generation. The
sections below describe the shape of a feature; to read them against real files, generate a project
with `--auth local` or `--auth external` beside this one.
<!--#elseif (todoLists)-->
<!--#else

**This project was generated without that example** (`--examples notes` or `none`), so the files
sections 1 to 6 open are not in this tree. A project generated with the default `--examples all`
holds them: generate one beside this one to read along, or read those sections as a description of
the shape, which the generator below writes for you either way.
#endif-->

The rule behind every step: **a layer only speaks its own language.** The domain
never imports EF; the API never imports EF Core either, only the application
layer's DTOs and ports. `Tests/Architecture/AppTemplate.Architecture.Tests` enforces
this, so a shortcut here is a failing architecture test rather than a silent drift.
Those rules are tests, not compiler checks: `dotnet build` says nothing about them,
so run `dotnet test Tests/Architecture` before you push. Where a step below can only
be got right by knowing which rule reads it, the rule is named on the spot.

Two placeholders run through the whole document. **`<Feature>` is the folder, in the
plural** — `TodoLists`, `Reminders`, `Files` — and **`<Aggregate>` is the type, in the
singular** — `TodoList`, `Reminder`, `StoredFile`. Folders under `Features/` carry the
feature; file and type names carry the aggregate. `TodoListErrors` lives in
`Features/TodoLists/Model/Errors/`, and nothing in the tree is called `TodoListsErrors`.

## Choose the shape first

**Does the feature have rules that protect a state?** A forbidden transition, uniqueness within a
collection, a cap, a consistency between fields. The answer decides the shape, and nothing else does
— not how many operations the feature has, not whether it is owned, not whether it is versioned.

| | Simple feature (no state to protect) | Rich feature (state to protect) |
|---|---|---|
| Example | `Notes` | `TodoLists` (and `Reminders`, `Files`) |
| Domain | nothing | aggregate, value objects, events |
| Application | one service (`INoteService`, one method per operation) over a store port and a queries port | one use case per operation, a repository port in the domain, a queries port |
| Persistence | the EF record is the model; the store projects to DTOs | record, mapper and tracker, so the domain stays free of EF |
| Validation | FluentValidation | FluentValidation, and the domain's invariants |
| Commit | `IUnitOfWork` | `IUnitOfWork`, the same boundary |

Both are first-class. Neither is the old way, and neither is the shortcut: giving an aggregate to a
feature that has no rule makes it pay for a mapper, a tracker and a repository to guard nothing, and
giving a service to one that has a rule leaves the rule nowhere to live. Three things hold in both
shapes — CQS (a read stages and commits nothing; separate read and write methods are enough, two
models are not required), the commit boundary, and the modular rule (a feature reaches another only
through that feature's ports and events).

The question is also the way out. A simple feature that acquires a rule moves to the rich shape by
the path in [section 8](#8-from-simple-to-rich--the-promotion-path), which is the only bridge between
them. Sections 1 to 6 below build the rich shape with `TodoLists`; [section 7](#7-the-simple-shape--the-notes-example)
builds the simple one with `Notes`. **The generator below writes either**: `--model record` is the
simple shape and `--model aggregate` is the rich one.

## The generator

**There is a generator, and it writes the shape so this document can be about the thinking.** It has
five choices, which are the five things a feature actually varies by, and nothing else is a switch:

```bash
dotnet run Tools/Tasks.cs new-feature <Features> <Feature> --access owner|shared \
    [--deletion delete|anonymise] \
    [--operations get,list,create,update,delete] \
    [--model record|aggregate] \
    [--concurrency none|etag]
```

`<Features>` is the folder in the plural and `<Feature>` the type in the singular: the two names the
walkthrough below calls `<Feature>` and `<Aggregate>`. Options follow the two names untouched from the launcher to `Tools/NewFeature.cs`, which
is what reads them; `dotnet run Tools/NewFeature.cs <Features> <Feature> ...` is the same command.

| Choice | Default | Meaning |
|---|---|---|
| `--access` | **none: required** | `owner` scopes every operation to the caller's id, read from `ICurrentUser` and never from a body, so another caller's record is answered exactly like a missing one. `shared` has no owner column and no filter: every authenticated caller reaches the same records. Neither is anonymous access, because the API still denies by default and the service still refuses a caller it cannot identify. |
| `--deletion` | **none: required with `--access owner`** | The fate of the feature's rows once the account that owns them is deleted, which the account cannot decide for data it no longer owns. `delete` removes them; `anonymise` rewrites what identifies the owner and keeps the rows. Either way the generator writes the consumer of `UserAccountDeleted` that applies the policy, both its faces in one class (section 2 below says what binds them by hand), and in `--model aggregate` the `Anonymise()` domain method that policy needs. Refused with `--access shared`, which holds no user data. |
| `--operations` | all five | Any subset of `get`, `list`, `create`, `update`, `delete`. An operation left out produces no file, method, route or registration. Each read stages and commits nothing. |
| `--model` | `record` | `record` is the simple shape: one service, a write port (`I<Feature>Store`) and a read port (`I<Feature>Queries`), and the EF record is the only model. `aggregate` is the rich shape: a domain aggregate with its repository port, one use case per operation, a queries port, and a record, mapper and tracker. The question in [Choose the shape first](#choose-the-shape-first) decides, and nothing else does. |
| `--concurrency` | `none` | `none`: an update replaces what is stored whatever its state, so **of two concurrent writers the last one wins**; the API publishes no `ETag` and reads no `If-Match`, and says so in its comments. `etag`: a persisted version (PostgreSQL's `xmin`), an `ETag` on reads, `If-Match` on writes, and a stale write refused at the database. |

A port is one capability, and staging writes is not reading, which is why a `record` feature has a store
and a queries port rather than every operation in a single port. A port whose operations are all
left out is not written: with only `get` and `list` there is no store, and with only `delete` there is
no queries port. A feature that owns user data is the exception on both sides: its account-deleted
reaction reads through the queries port and writes through the store (or the repository) whatever
the operations are, so an owner feature always has both halves.

**The combinations that are refused**, each before anything is written and each with the change that
fixes it:

- `create` without `get`: the `201` carries a `Location`, and the route it names is the one of `get`.
- `--access owner` without `--deletion`: the fate of user data is not a default to slip into.
- `--deletion` with `--access shared`: no caller owns the rows, so there is no fate to declare.
- `--concurrency etag` with `update` or `delete` but no `get`: nobody could read the version to send it back.
- `--model record --concurrency etag` with neither `update` nor `delete`: a version nothing checks.
- `--model aggregate` without `--concurrency etag`. An aggregate is loaded whole and written whole,
  so a write that overwrites a newer one can break a rule each write kept alone, and the tracker that
  writes it refuses to work without a version. If last-write-wins is what the feature wants, it has
  no state to protect and `--model record` is the shape.
- `--model aggregate` with no `create`, `update` or `delete`: a feature that only reads has nothing to protect.

**The command that corresponds to `Notes`** is the one that chooses what `Notes` chose:

```bash
dotnet run Tools/Tasks.cs new-feature Notes Note --access owner --deletion delete --concurrency etag
```

The record shape and all five operations come from the defaults. **An optionless command is not
`Notes`**: `new-feature Widgets Widget --access shared` writes a shared, unversioned, last-write-wins
feature, and `--access` has no default precisely so that this is never an accident. What is written
for the `Notes` command is the code of `Notes` apart from its data: the example's `Title` and `Body`
are `Name` and `Description` here, with limits of 200 and 2000 characters, and the example keeps tests
the generator does not write (the collection policy, the sort map, the controller and the PostgreSQL
integration tests).

**Undoing it.** `dotnet run Tools/Tasks.cs remove-feature <Features>` reads the manifest the generator wrote and
reports, writing nothing: the files the feature owns, the lines elsewhere that register it, configure its EF model, mention it
in a migration or the snapshot, or use its types (confirmed), the lines that only contain its name (possible), and the checks to run
afterwards. Adding `--apply` deletes the generated files, the manifest and the folders left empty, but only when every file still
hashes as written, nothing was added to the feature's folders, and none of the six edits above (nor any other use of the feature)
exists yet. Otherwise it refuses before deleting anything and prints the same report. A use of its types is
confirmed only by a name the feature itself declares, so `OrderBy` beside an `Orders` feature, or a type's name in a
string or a comment, is a possible match and blocks nothing. A migration is never touched. The feature page the manifest
records is deleted with the rest, edited or not, and a page already deleted blocks nothing; its row in `docs/README.md` is
yours to remove.
The first owner feature of a project generated with no feature also holds the account-deletion files every owner feature
shares (below): `--apply` deletes them with it only when no other owner feature and none of the five registrations printed
for them still use them, and refuses otherwise.

**What it writes**, for the shape and operations chosen:

- Application: the DTOs, the errors, the input and collection policies where they are used, the
  validator, the ports, and either the service (`record`) or the use cases with their commands and
  queries (`aggregate`). An owner feature also gets the consumer of `UserAccountDeleted` that
  applies its declared policy, the `ListDataOwnerIdsAsync` the sweep asks its queries port for, and
  the owner-wide write the policy applies (`StageRemovalForOwnerAsync` / `StageAnonymisationForOwnerAsync`
  on the store, `GetForOwnerAsync` on the repository, `Anonymise()` on the aggregate).
- Domain, for `aggregate` only: the aggregate and its repository port. It holds a name and a
  description with the length rules of a new write, and nothing that the feature has not been asked
  for: the rules it protects are yours to write.
- Persistence: the record, its EF configuration, and the store, the queries and the sort map (`record`),
  or the mapper, the tracker and the repository (`aggregate`).
- Api: the controller, the request and response contracts, and the mapping.
- Tests whose assertions hold for any feature of the shape, and none that would pass for any code: the
  errors, the validator, the EF mapping and the response mapping for both models; for `record` the
  service (anonymous callers refused and nothing read or written for them, an absent record not found,
  invalid content staging nothing); for `aggregate` the aggregate's rules and the mapper's round trip.

A project generated with no feature ships none of the account-deleted reaction, and its first owner
feature brings the missing pieces with it: `IDeletedAccountDataPolicy` and `IUserAccountDirectory`
in `Application.Core`, the sweep use case, the background registration of the sweep, and the
account-directory adapter over the local accounts or the external-identity mapping. A project that
already has a feature — or a tree without accounts, which is refused — gets none of those. Those
files are listed in the first owner feature's manifest, so `remove-feature` refuses to remove that
feature while a later owner feature, one composed by hand, or a registration of the sweep or of the
adapter still uses them. There are two ways out: remove the other owner features first, after which
the first one goes with the shared files and the project is back to the one generated with no
feature; or remove the first one by hand and keep the seven files, which no manifest owns from then
on.

**It creates files and edits none**, because registration is opt-in per feature precisely so that a
feature nobody composes is visibly registered nowhere. It prints the edits instead — the three in
`AppDbContext`, the `Add<Features>()` of `ApplicationModule` (with, for an owner feature, the two
faces of its account-deleted policy) and of `PersistenceModule`, the call in `Program.cs`, and the
two test classes that list every feature — and then the migration step. Until the first of them is
made the solution does not compile, because the generated record configuration refers to the schema
constant `AppDbContext` does not yet hold; it compiles again once that edit is in, and the feature
composes once all of them are. When it brought the
account-deleted reaction with it, five more edits are printed: the `AddDeletedAccountSweep()` of
`ApplicationCoreModule` and its call in `Program.cs`, the `AddDeletedAccountSweepJobs()` call of
`BackgroundJobsModule`, the directory adapter in the composition of the accounts, and in the
architecture tests' `HostComposition` the sweep in each composition with a double standing in for
the directory, without which the container tests cannot resolve the feature's consumer. It does not
run EF: while the project is unreleased the repository keeps one `InitialCreate` per context, so the
step is to regenerate it, and after a release it is a named migration.

**Before writing anything** it checks every name and option, refuses a combination that would not hold
together, and refuses to write if the feature folder, the manifest or any file it would create
already exists. It never overwrites a file and, if a write fails half way, removes what it had
created. It reads the product prefix off the tree, so it works after the template has been renamed.

**The creation manifest** is `.feature-manifest/<Features>.json`, a tracked file of the project beside
the code it describes: the options, and every created file with the SHA-256 of what was written. A
later command that removes a feature can tell what the generator made from what has been edited
since. Nothing in the template ships one, so nothing excludes the folder from the template.

**The feature page** is `docs/features/<features-kebab>.md`, written from
[`_templates/feature.md`](_templates/feature.md) with the feature's name, its access, its shape, its endpoints and its
test folders filled in, and one line under each heading saying what the section holds. The manifest records it apart
from the hashed files, because the page exists to be edited. A page already there is left as it is and not recorded,
and a tree without the page template gets none. The command prints the row to add under `## Features` in
`docs/README.md`.

The sections below describe what to put in what it wrote.

**Which shape**

- [Choose the shape first](#choose-the-shape-first) — the one question, and what each answer builds

**The vertical, in order** (the rich shape)

1. [Domain — the aggregate](#1-domain--the-aggregate)
2. [Application — the use case and its port](#2-application--the-use-case-and-its-port)
3. [Persistence — model, mapping, tracker, repository](#3-persistence--model-mapping-tracker-repository)
4. [API — controller and contracts](#4-api--controller-and-contracts)
5. [Tests](#5-tests)
6. [Migration](#6-migration)

**When your feature is not shaped like `TodoLists`**

<!--#if (files)-->
- [Not only rows — the `Files` example](#4b-when-the-feature-is-not-only-rows--the-files-example)
  <!--#endif-->
- [No child entities — the `Reminders` example](#4c-when-the-aggregate-has-no-child-entities--the-reminders-example)
- [It shares, caches, or runs on a timer](#4d-when-the-feature-shares-caches-or-runs-on-a-timer)
- [The closed lists a new feature lands in](#5b-the-closed-lists-a-new-feature-lands-in) — the
  dictionaries that fail the build until they name your feature

**When the feature has no state to protect**

- [The simple shape — the `Notes` example](#7-the-simple-shape--the-notes-example)
- [From simple to rich — the promotion path](#8-from-simple-to-rich--the-promotion-path)

## 1. Domain — the aggregate

`Src/Domain/AppTemplate.Domain/Features/<Feature>/`

```
Model/Entities/<Aggregate>.cs         the aggregate root
Model/ValueObjects/<Name>.cs          validated primitives (record types)
Model/Events/<Thing>DomainEvent.cs    what the aggregate raised
Repositories/I<Aggregate>Repository.cs   the repository contract, in domain types only
```

Everything the aggregate is built *from* comes from one project inwards,
`AppTemplate.Domain.Core`: the root derives from its `AuditableAggregateRoot<TId>` — or from
`AggregateRoot<TId>` when it wants neither audit columns nor a concurrency token — a child entity
from its `Entity<TId>`, each event implements its `IDomainEvent`, and a violated invariant throws
its `DomainException`. The audit values and the version are **not written out**: the base holds
them and implements `IAuditable` and `IVersioned` explicitly, so the store writes them and nothing
holding the aggregate can. An owned aggregate also declares `IOwnedAggregate`, which is what lets
the shared ownership gate below check it. A feature adds nothing to that project. It is vendored rather than
published — it references nothing at all, and every public member of it is documented — so the primitives are something you read the signatures of and build on, not
something a new feature negotiates with.

`TodoList` (`Model/Entities/TodoList.cs`) is the reference: a private list of items, a
factory (`Create`) and a rehydration path (`Rehydrate`) that both run the same
invariants — unique titles, an item cap, a non-empty owner — so a row loaded from
the database can never produce an aggregate that breaks its own rules. Put every
invariant in the constructor, the factory, *and* `Rehydrate`; missing one of the
three is exactly the bug `TodoListRehydrationTests` exists to catch.

The repository contract lives in `Repositories/`, not `Application`, because it is
stated entirely in domain types:

```csharp
public interface ITodoListRepository
{
    Task<TodoList?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Add(TodoList todoList);
    void Remove(TodoList todoList);
}
```

`AdapterVisibilityTests` recognises a repository contract by a namespace ending in
`.Repositories` — that is the whole rule, so follow the folder name exactly. Seal the
aggregate root: `DomainModelTests.AggregateRoots_AreSealed` requires it.

<!--#if (aggregates)-->
**An event nothing listens to has to be a decision, not an oversight.** The first
`Model/Events/<Thing>DomainEvent.cs` you add turns
`DomainEventTests.EveryDomainEvent_IsEitherConsumedOrListedAsUnconsumed` red until one
of two things is true: some `IDomainEventConsumer<T>` in the application layer handles
it, or its type name is written into the `_deliberatelyUnconsumed` array at the top of
`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/DomainEventTests.cs` with the
reason nothing consumes it. Publishing a fact no consumer wants yet is perfectly good
design; leaving the reader unable to tell that from a consumer somebody forgot is not,
and the array is where the difference is stated.
<!--#endif-->

## 2. Application — the use case and its port

`Src/Application/AppTemplate.Application/Features/<Feature>/`

A feature is created there and only there. `AppTemplate.Application` is `Features/` and, today, no
`Common/` — not because it may not have one, but because nothing in this template's examples is yet
shared by several features *as business*. That is what a `<Layer>/Common/` is for: a DTO two
features answer with, a policy that spans them, a value object three features spend. The sorting
test is one question — does it know a feature? If it names one, or would have to the moment a second
feature used it, it goes in this project's `Common/`. If it does not and never will, it goes one
project inwards. Everything the feature is built *from* is one project inwards,
in `AppTemplate.Application.Core`: `Result` and `Error`, the `IUseCase` marker the named interface
derives from, `PageRequest`, `SortOrder` and the rest of the paging vocabulary,
`CollectionBinding.Bind` and `FeaturePageRequest<TFilter>`, `OwnedAggregate.Require`,
`VersionPrecondition` and `Versioned<T>`, the validation extensions, `DomainGuard`,
`IDomainEventConsumer`, and the cross-cutting ports — the clock, the caller's identity, the commit
boundary, the mail relay, the leader lease, the idempotency store. A feature adds nothing to that
project; if a new mechanism looks like it belongs there, that is a decision to argue for, not a
file to move.

```
UseCases/Commands|Queries/<Operation>/         one folder per operation:
  <Operation>Command.cs                        the command or query record
  I<Operation>UseCase.cs                       the named interface
  <Operation>UseCase.cs                        the use case
  <Operation>CommandValidator.cs               FluentValidation, scoped to this operation
  <types used only by this one operation>      e.g. a response record no other operation returns
Ports/<Port>/                  a port interface, together with the messages that cross it
Model/Dtos/<Name>Dto.cs        read models more than one operation shares
Model/Errors/<Aggregate>Errors.cs    the feature's Error catalogue
Model/Mapping/                 aggregate -> DTO projections
Model/Policies/                the collection whitelist, and every other rule the feature
                               enforces; each one named ...Policy
Services/                      internal collaborators shared by more than one use case
Services/Consumers/<Event>/    domain-event consumers, if the feature has any
Helpers/                       pure static functions with no more precise nature, if any
```

A folder is only present when it has content — a feature with no domain-event consumer
has no `Services/Consumers/`, and one whose only consumer is one file keeps it flat there rather
than in an `<Event>/` folder. `Model/Dtos/` holds only shapes more than one operation returns; a
response type only one operation produces stays in that operation's own folder instead.
A type that appears in a port's signature never moves into a use case's folder, however
many use cases call that port — otherwise `Ports/` would depend on `UseCases/`.
`TodoListFilter` is the real example: it is half of `ITodoListQueries`'s parameter, so it
lives in `Ports/TodoListQueries/`, not inside any one query's own folder. The other half,
`FeaturePageRequest<TodoListFilter>`, is not the feature's to place at all — it comes from one
project inwards.

**A feature is registered by its own `AddX()` in `ApplicationModule`, which a host calls or does
not.** `AddTodoLists()`, `AddReminders()`, `AddFiles(configuration)` and `AddNotes()` are the ones the
template ships, fewer when it was generated with `--examples` or `--files false`, and there is
deliberately no call that adds all of them: that is what makes a feature removable — delete its
folder and its one line in each host, and nothing else claims to have registered it. So a new
feature means a new `AddX()` here and a line in each `Program.cs` that offers it. Inside the method
the vertical's use cases and validators are discovered by namespace.

**Two of those folders hold things nothing discovers for you, and both are registered by hand in
that method.** A domain-event consumer is bound to its event with
`services.AddDomainEventConsumer<TEvent, TConsumer>()`; the binding is written out rather than
scanned, so a consumer nothing reaches is an absence you can see in the file instead of a silence at
runtime. A collaborator under `Services/` has no request or response shape of its own, so the
marker-based discovery below never sees it either: bind it with its own
`services.AddScoped<TContract, TImplementation>()` in the same method. The collaborator every owned
feature has is the load gate, and it is four lines: narrow the caller with `RequireUserId()`, then
hand `OwnedAggregate.Require` the aggregate the repository answered, that caller, **this feature's
own not-found error** and the precondition. Which error says "absent" stays the feature's decision —
that is why the gate takes one instead of minting one — and it is what answers a non-owner exactly
as an unknown id. Skip either line and the code
still compiles — the consumer simply never fires, and the use case that takes the collaborator
throws on its first resolution.

<!--#if (anyFeature)-->
**A feature that owns user data also declares what a deleted account means for its rows**, and the
generator asks which fate and writes the reaction: one consumer of `UserAccountDeleted` that also
implements `IDeletedAccountDataPolicy` (both faces in one class: the consumer is the fast path, the
policy face is what the sweep calls), re-deriving the precondition through `IUserAccountDirectory`
— the account must be gone — before it writes anything, and committing per owner through the
feature's own `IUnitOfWork`. What stays by hand is the binding of both faces in the feature's
`AddX()` — the `AddDomainEventConsumer<UserAccountDeleted, …Consumer>()` line and the
`AddScoped<IDeletedAccountDataPolicy, …Consumer>()` line — because registration is opt-in
everywhere else and this is no exception. Deleting is the template's own features' choice;
anonymising is the same two faces writing rows instead of removing them. The sweep that guarantees
the fact reached a feature is already composed — nothing to add — and a feature that holds no user
data declares no policy at all.
<!--#endif-->

A port is a capability, not a façade, and two architecture rules hold it to that.
`PortConventionTests` refuses any port declaring more than **six** operations: six lets a
port cover one intent — the authentication ports group an account's session tokens, or its
mailed tokens, that way — and a seventh means a second intent that belongs in a port of
its own. It also refuses a feature port that *every* use case in its vertical
depends on, because a vertical whose use cases are all wrappers around one collaborator
has put its logic in the collaborator. Neither rule applies to the cross-cutting ports
in `AppTemplate.Application.Core/Common/Ports/` — the clock and the caller's identity may
legitimately be needed everywhere.

One class per use case, plus **exactly one named interface** deriving from
`IUseCase<TRequest, TResponse>` (or `IUseCase<TResponse>`):

```csharp
public sealed record CreateTodoListCommand(string Name);

public interface ICreateTodoListUseCase
    : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;

public sealed class CreateTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        CreateTodoListCommand command,
        CancellationToken cancellationToken = default)
    {
        …
    }
}
```

Its body requires the caller, validates the command, builds the aggregate, stages it, calls
`SaveChangesAsync` and projects the written aggregate back to a DTO.

The named interface is not decoration: `ApplicationCoreModule.AddUseCases`, which each feature's
`AddX()` calls over the types in that vertical's namespace, registers every `IUseCase`
implementation under the **one** other interface it declares. Zero or several named interfaces
throws from that call itself, at registration, before the container is ever built —
rather than surfacing as a 500 on first request the way a missing registration would.
Controllers depend on the named interface, never on the concrete class.

Return `Result`/`Result<T>` carrying an `Error` with a stable `code` for every
expected failure (not found, not owner, validation). Reserve `DomainException` for a
violated invariant, and either let it become a 400 through `GlobalExceptionHandler`
(a bare net, not a path any use case is meant to rely on), or run the call through
`DomainGuard` to turn it into a 409 `Result` when the use case *expects* the invariant
to sometimes refuse — never a 500: that status is for a bug nothing anticipated, not
for a caller driving an aggregate into a state the model forbids.

### If the feature has a collection endpoint

Sorting, filtering and paging are already built, once, in
`AppTemplate.Application.Core/Common/Collections/`. A feature does not implement them — it
**declares what it allows**:

```
Model/Policies/<Aggregate>CollectionPolicy.cs        the sortable whitelist and the bounds
Ports/<Aggregate>Queries/<Aggregate>Filter.cs       the typed filter surface + its validation
```

The filter travels with the read-side port it is the parameter of — `TodoListFilter` lives in
`Ports/TodoListQueries/`, next to `ITodoListQueries` itself — because it is that port's signature,
not one use case's private concern. **There is no per-feature page-request type**: the port takes
`FeaturePageRequest<TFilter>`, which only `CollectionBinding.Bind` can build, so a request that
skipped validation cannot exist. The feature's query record declares `ICollectionQuery` — its five
paging members, and so the OpenAPI parameters it publishes, are unchanged by doing so.

The policy is the whitelist, and it lives in the feature's own `Model/Policies/` folder rather than
beside `ICollectionPolicy` in `AppTemplate.Application.Core/Common/Policies/`, because only the
feature knows which of its columns are cheap to order by:

```csharp
public sealed class TodoListCollectionPolicy : ICollectionPolicy
{
    public const string NameField = "name";
    public const string CreatedAtField = "createdAt";

    public const string LastModifiedAtField = "lastModifiedAt";

    public static readonly TodoListCollectionPolicy Instance = new();

    public IReadOnlyList<SortableField> SortableFields { get; } =
    [
        SortableField.Keyset(NameField),
        SortableField.Keyset(CreatedAtField),
        SortableField.OffsetOnly(LastModifiedAtField),
    ];

    public string DefaultSort => "createdAt:desc";
    public int MaxSortTerms => 3;
    public int MaxPageSize => 100;
    public int DefaultPageSize => 20;
}
```

Four rules, and they are the whole reason this is safe:

1. **A field on the whitelist gets an index.** Putting a name in
   `SortableFields` is a promise that ordering by it is cheap, so add a composite
   index `(<owner or tenant key>, <field>, Id)` in the feature's
   `IEntityTypeConfiguration` — ending in the tiebreaker, so both the sort and the
   keyset comparison are index-ordered. If you are not willing to add the index,
   the field does not belong on the list.
2. **`SortableField.Keyset` only for a non-nullable column.** A keyset comparison
   against `NULL` is neither true nor false, so the row the cursor was minted from
   would be skipped instead of resumed from. A nullable column is
   `SortableField.OffsetOnly`, and asking for it with `paging=cursor` is a `400`.
3. **`DefaultSort` is written in the caller's own syntax** and parsed by the same
   `SortOrder.Parse` that parses caller input — so a typo in a feature's default
   fails a test instead of shipping. `CollectionContractTests.EveryCollectionPolicy_IsInternallyConsistent`
   in `Tests/Architecture/AppTemplate.Architecture.Tests` asserts exactly that, for
   every policy, automatically.
4. **The filter is typed, never a string that becomes a predicate.** Each filter
   is a named parameter with a CLR type and a validating factory returning
   `Result<T>`; free text goes through `SearchTerm`, which bounds its length. See
   [`DECISIONS.md`](DECISIONS.md) for why there is no expression language.

The feature's binder is what is left once the shared half is shared: its policy, its filter and
which of its sortable fields holds an instant.

```csharp
public static Result<FeaturePageRequest<TodoListFilter>> Bind(GetTodoListsQuery query) =>
    CollectionBinding.Bind(
        query,
        TodoListCollectionPolicy.Instance,
        () => TodoListFilter.Create(query.Search, query.CreatedAfter, query.CreatedBefore),
        TodoListCollectionPolicy.CreatedAtField);
```

```csharp
Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
    UserId ownerId,
    FeaturePageRequest<TodoListFilter> request,
    CancellationToken cancellationToken = default);
```

The translation to SQL is the persistence half, in
`Features/<Feature>/Access/Queries/<Aggregate>SortMap.cs`: an exhaustive `switch` on the
canonical field name returning a typed key selector, a `default` arm that throws
because the use case should already have refused, and a mandatory
`.ThenBy(record => record.Id)` on every order. Copy `TodoListSortMap` — the shape
is the point, and `Tests/Architecture` enforces the parts of it that can be
checked mechanically.

## 3. Persistence — model, mapping, tracker, repository

`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/<Feature>/`

```
Rows/Models/<Aggregate>Record.cs             the EF row — no behaviour, no invariants
Rows/Configurations/<Aggregate>RecordConfiguration.cs   IEntityTypeConfiguration<T>
Rows/Mapping/I<Aggregate>Mapper.cs / <Aggregate>Mapper.cs
Rows/Tracking/I<Aggregate>Tracker.cs / <Aggregate>Tracker.cs
Access/Repositories/<Aggregate>Repository.cs implements the domain's repository contract
Access/Queries/<Aggregate>Queries.cs         read-side projections, if the feature has any
```

**The record is not the aggregate.** `TodoListRecord` is a settable class with no
rules; `TodoList` is the aggregate with all of them. This split is
the subject of [`CONVENTIONS.md`](CONVENTIONS.md#design-conventions)'s Persistence paragraph — read
it before changing any of the four pieces below,
because they exist specifically to keep the split intact:

1. **Record** (`Rows/Models/`) — implements `IAuditable`; carries the EF-visible shape
   (foreign keys, the `xmin`-backed `Version` column, audit stamps) and nothing the
   domain does not also need to persist.
2. **Configuration** (`Rows/Configurations/`) — `ToTable`, keys, indexes, string lengths
   read from the domain's own constants (e.g. `TodoListName.MaxLength`) so the
   column and the invariant cannot drift apart.
3. **Mapper** (`Rows/Mapping/`) — the one place that knows both shapes: `ToAggregate`
   (row → domain, total), `ToNewRecord` (domain → row, total, exercised by a
   round-trip fidelity test), and `WriteTo` (domain → row, deliberately *partial* —
   it never touches the columns the store owns: audit stamps, the concurrency
   token). Stateless, EF-free, registered as a singleton.
4. **Tracker** (`Rows/Tracking/`) — the identity map EF is not maintaining, because EF
   only sees records, not aggregates. One instance per request resolved under
   **three** contracts (`I<Aggregate>Tracker` for the repository,
   `IAggregateFlusher` for the flush interceptor, `IDomainEventSource` for the
   dispatch interceptor) — see the registration note below, it is the single most
   common way to get this feature wrong.
5. **Repository** (`Access/Repositories/`) — implements the domain's `I<Aggregate>Repository`.
   Consults the tracker first (identity map), then queries with
   `.Include(...).AsSplitQuery()` for anything with child collections, maps, and
   registers the result with the tracker. Never calls `SaveChangesAsync` — that is
   `IUnitOfWork`'s job, called once by the use case.

Registration is in two files, and both are required. The first is
`PersistenceModule.AddPersistenceModule`, which wires the container — add an
`Add<Feature>Feature` method of your own next to the existing `AddTodoListsFeature`,
and call it from `AddPersistenceModule` alongside the others:

```csharp
services.TryAddSingleton<ITodoListMapper, TodoListMapper>();

services.TryAddScoped<TodoListTracker>();
services.TryAddScoped<ITodoListTracker>(p => p.GetRequiredService<TodoListTracker>());
services.AddScoped<IAggregateFlusher>(p => p.GetRequiredService<TodoListTracker>());
services.AddScoped<IDomainEventSource>(p => p.GetRequiredService<TodoListTracker>());

services.TryAddScoped<ITodoListRepository, TodoListRepository>();
services.TryAddScoped<ITodoListQueries, TodoListQueries>();
```

That last line is the read-side port from section 2, and it is the one to check twice:
leaving it out breaks nothing the write endpoints touch, so everything looks right
until the list endpoint fails to resolve on its first request.

**Register the tracker once, as a factory under each contract — never three
separate `AddScoped` calls.** Three registrations mean three instances: the
repository fills one identity map, the flush interceptor flushes a different, empty
one, and every write persists nothing, silently, with no error anywhere.
`SharedInstanceRegistrationTests` exists to catch exactly this, and it will fail
loudly if a new feature gets it wrong — do not treat a failure there as a false
positive.

The second file is
`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs`,
and it is where the mapping becomes real. **`PersistenceModule` applies no EF
configuration at all**; `AppDbContext` names each one by hand, so a feature needs three
things written there:

1. **A schema constant.** No default schema is set on the model, so a table that names
   none lands in the connection's default schema rather than the feature's. Add
   `public const string <Feature>Schema = "<feature>";` beside `TodoSchema`,
   `RemindersSchema` and `FilesSchema`, and read it from your
   `IEntityTypeConfiguration`'s `ToTable` call. A feature that owns its own schema is
   also a feature whose removal is a deleted migration file rather than a drop.
2. **A `DbSet`.** `internal DbSet<<Aggregate>Record> <Feature> => Set<<Aggregate>Record>();`
   — internal, like every other set here, because nothing outside the assembly has any
   business naming a storage shape.
3. **The `ApplyConfiguration` call.** Add
   `builder.ApplyConfiguration(new <Aggregate>RecordConfiguration());` to
   `OnModelCreating`, in the block for your feature.

Miss the third and the configuration is inert. Nothing about that is loud on its own:
the build is green, the architecture rules that read types are green, and
`dotnet ef migrations add` writes an **empty** migration, because the model and the
snapshot omit the entity in exactly the same way — so even `PendingModelChangesTests`
agrees. The first thing that disagrees is a query at runtime.
`PersistenceModelTests.EveryEntityTypeConfiguration_IsAppliedByTheContext` is the rule
that closes that gap: it reads the `IEntityTypeConfiguration` implementations in the
module, reads the `ApplyConfiguration` calls out of `AppDbContext.cs`, and names any
configuration that appears in the first list and not the second. Its counterpart
checks the other direction. Run the architecture project and you will be told which
configuration you forgot; skip it and you will find out from a `500`.

## 4. API — controller and contracts

`Src/Presentation/AppTemplate.Api/Features/<Feature>/`

```
Controllers/<Feature>Controller.cs
Contracts/Requests/<Verb><Aggregate>Request.cs    request records the controller binds
Contracts/Responses/<Verb><Aggregate>Response.cs  response records the controller returns
Contracts/Mapping/<Aggregate>ResponseMapping.cs   application DTO -> response records
```

The controller depends on named use-case interfaces only, maps requests to
commands, and turns `Result`/`Error` into the right status via `ErrorMapping` (see
`ApiControllerBase`). Every endpoint is authenticated by default — `Program.cs`
installs a default-deny fallback policy — so an anonymous endpoint needs an
explicit `[AllowAnonymous]`, not the absence of an attribute.

**Three conventions every controller here shares, and a new one is expected to.**
`ProducesResponseType` only ever adds, so statuses are declared action by action: 409 on
a write that can violate an invariant or lose a race, 404 where an aggregate has to be
found first, 400 wherever a body, a query string or an `If-Match` header is read. 401
goes on the controller when every action needs authentication, and 413, 415, 429 and 500
come from `ApiControllerBase`. **A conditional request is decoded here and compared in
the use case** — the controller turns `If-Match` into a `VersionPrecondition` and hands
it over, because only the use case holds the aggregate it loaded and can compare without
leaving a window for someone else to commit; the transport verdicts on a malformed or
missing header come from `ApiControllerBase.ReadPrecondition`. **And a write answers with
the representation it produced and that representation's new `ETag`**, so a caller never
re-reads what it just changed in order to keep writing.

**Each feature chooses its write style.** A write says in its route or body what it does — a
complete representation through `PUT`, or a named operation (`RenameX`, `CompleteX`,
`RescheduleX`) — rather than a bag of optional fields. `PATCH` is allowed where it fits, and
[`API-CONVENTIONS.md`](API-CONVENTIONS.md) states the shape it takes here: JSON Merge Patch,
applied through the aggregate's own methods and invariants, never a blind property copy.
**And an `[AllowAnonymous]` is two edits, not one**: the attribute on the action, and
the action's method name added to the `_anonymousActions` array in
`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/HttpSurfaceTests.cs`. That
array is the whole of the public surface written down in one place, so opening an
endpoint is a line somebody reviews rather than an attribute nobody notices.

<!--#if (files)-->
## 4b. When the feature is not only rows — the `Files` example

Everything above assumes a feature whose whole state is rows in the one database.
`Files` is the third worked example and the one that breaks that assumption: half of
a stored file is an aggregate in PostgreSQL, the other half is bytes in an
S3-compatible bucket or a local directory. Read it when your feature has a second place where its
state lives — a bucket, a search index, a queue.

Five things it does differently, and none of them is optional once your feature has
two stores:

**The second store is behind a port, and the port speaks no domain type.**
`IFileContentStore` (`Src/Application/AppTemplate.Application/Features/Files/Ports/FileContentStore/`)
takes a `string` key and a `long` size, never an `ObjectKey` or a `FileSize` —
`StorageVocabularyTests` refuses a `Store` whose signature names anything under
`AppTemplate.Domain.Features`, and that refusal is what the word `Store` means here.
The adapter lives in its own module, `AppTemplate.Infrastructure.Storage`, which
follows the same nature-word vocabulary as everything else: `Common/{Budgets,
Factories, Options}` and `Features/Files/{Inventories, Options, Scanners, Stores,
Transfers}`, one plural per nature of file and nothing else inside. A new infrastructure
module of your own gets the same treatment — and so does a new project in any layer, because
`LayoutConventionTests.EveryProjectOnDisk_HasAVocabularyOfItsOwn` reads every project under `Src/`
off the disk rather than the infrastructure modules alone, and will not
let it build until its vocabulary is written into [`PROJECT-LAYOUT.md`](PROJECT-LAYOUT.md)'s folder
map and into the rule's own dictionary. An entry of `null` is how a project says it has no such folder at
all, and that claim is checked in both directions: the folder appearing where the entry says none
fails, and so does the folder vanishing from under a list of words.

**The two halves meet in the application layer and nowhere else.** No repository reaches
the bucket, no adapter reaches the database. `IStoredFileDepositService` is where the row
and the object are compared, for `ConfirmFileUploadUseCase` and `UploadFileUseCase` alike.

**Large bytes do not travel through the API.** Registering returns a signed upload grant
and the client writes to the store directly; reading returns a signed download grant and
the controller answers `302`. `RequestLimits:MaxRequestBodyBytes` is 65 536, and
`IdempotencyFilter` buffers and SHA-256s the entire body of every `[Idempotent]` `POST`.
The one exception is `POST /api/v1/files/uploads`, for small files and for the file system
store: not idempotent, and its limit raised to `Files:ApiUploadMaxBytes` by
`RequestBodyLimitAttribute` — the mechanism a feature of yours uses if it must accept a
large body, rather than raising the global limit.

**Consistency between the two stores is re-derived, not transacted.** There is no
distributed transaction and no tombstone: `DELETE` removes the row, and
`ReclaimOrphanedContentUseCase` deletes every object no row references. That shape —
an effect that re-derives its own precondition — is what lets this template keep
refusing an outbox, and any feature with a second store owes the same shape. The
deletion event is a fast path on top of it, never the guarantee. A sweep must also reach
everything: the orphan sweep resumes from a checkpoint rather than restarting at the
first key, and inspection defers a file it cannot decide on behind the others.

**Anything the second store holds is untrusted until something has looked at it.**
`InspectDepositedFilesUseCase` checks the declared media type against the bytes and
scans the content before a file becomes readable, in a background job rather than in the
request. See `SECURITY.md` for which threats that answers and which it does not.

**What a file may be is configuration, not code.** Each file is registered under a category
declared in `Files:Categories` — media types, maximum size, scan and signature switches,
visibility, upload modes and a quota per owner — and keeps it for life (see
[`CONFIGURATION.md`](CONFIGURATION.md#files)). A business feature that needs a file — an avatar, an
invoice attachment — stores the file's id as a plain `Guid` and has it registered under a category of
its own. It reaches `Files` only through that feature's application ports or its domain events,
never through `StoredFileRecord` or the `files` schema — "Features meet through ports and events"
in [`ARCHITECTURE.md`](ARCHITECTURE.md) holds for `Files` like for any other feature.

The cost is a state machine, which a single-store feature does not need: `Pending →
Deposited → Available`, plus `Quarantined`. Its invariants are in the constructor,
the factory **and** `Rehydrate`, like any aggregate here — and its enum values are
pinned by a test, because they are persisted as integers and renumbering them would
reinterpret every existing row without changing a single line of the schema.

<!--#endif-->
## 4c. When the aggregate has no child entities — the `Reminders` example

`TodoLists` owns items, and items own tags, so a good part of what section 3 asks for
exists only because of that ownership. `Reminders` is the same vertical with a flat
aggregate, and it is the closer comparison for most new features: read it to see which
of the pieces above you actually need.

What it does not have is the point. The domain side is four files —
`Model/Entities/Reminder.cs`, `Model/ValueObjects/ReminderState.cs`,
`Model/Events/ReminderFiredDomainEvent.cs`, `Repositories/IReminderRepository.cs` — with no
child collection, so `ReminderRepository` loads a row and maps it, where
`TodoListRepository` needs `.Include(list => list.Items).ThenInclude(item => item.Tags)`
and `.AsSplitQuery()`. There is no `Model/Policies/` folder either: reminders are read one
item's worth at a time, so the feature has no collection endpoint, and therefore no
`CollectionPolicy`, no `Filter`, no request binder and no `SortMap`. Section 2b simply
does not apply to it. Skipping all of that is the default, not a shortcut — build the
collection machinery when a feature has a list to page, and not before.

Three things it does show that `TodoLists` does not. **The operation cap is a rule
about application ports, not about repository contracts.** `IReminderRepository`
is not counted, and that is legitimate: it lives in the domain, and
`PortConventionTests` reads only interfaces under
`AppTemplate.Application.Features.<F>.Ports`. **`ReminderFiredDomainEvent` is a live
entry in `_deliberatelyUnconsumed`**, with the reason written next to it — the worked
example of the decision section 1 asks you to make. **And the feature is driven from
two triggers**: `RemindersController` serves the HTTP side, while `FireDueRemindersUseCase`
is called by a background job, which is why the feature has ports for a notifier
(`Ports/ReminderNotifier/`), for its own metrics (`Ports/ReminderDiagnostics/`) and for
its own reads (`Ports/ReminderQueries/`); what it needs to know about a to-do item it asks
of `TodoLists`' `ITodoItemQueries`, never of that feature's rows — see "Features meet through
ports and events" in [`ARCHITECTURE.md`](ARCHITECTURE.md). A use case is a use case
whichever trigger calls it; nothing about the shape changes.

## 4d. When the feature shares, caches, or runs on a timer

Three seams a second feature meets that the first one does not, and each has one right
answer here.

**Something two features share.** The question is not "is it repeated" but **does it know
a feature?** If it names a business type, it goes in a `Common/` folder of the business project —
the domain project's for the domain half, the application project's for the application one. If it
names no business type and never would, it goes one project inwards, into the layer's `.Core`, and
a feature adds nothing to those.
<!--#if (tagging)-->
`Common/Tagging/` is the worked example of the first
answer: tagging is one rule for every feature that tags something, so `Tag` and `TagSet` sit in the
domain's `Common/Tagging/` and `TagValidation` in the application's, and the folder stays as long as
one tagged feature does. The test that put
them there is mechanical rather than a matter of taste — `TagSet` names `Tag`, and a
`.Core` project may name no business type.
<!--#else-->
The test is mechanical rather than a matter of taste: a `.Core` project may name no business type.
<!--#endif-->
What is *not* there is as instructive: the
ownership check every feature performs and the paginated read every feature exposes are
both duplicated and both agnostic, so they belong inwards and not here.

Adding a folder to either `Common/` means adding its word to the `Common/` vocabulary
`LayoutConventionTests` holds and to [`PROJECT-LAYOUT.md`](PROJECT-LAYOUT.md)'s folder map, and the entry
is checked in both directions — a folder appearing where the dictionary says none fails,
and so does a word whose folder has gone.

The infrastructure layer has the same two halves, and its agnostic one is
`AppTemplate.Infrastructure.Core`. A module may not reference a sibling, so a mechanism two
modules need goes there and both take it from one place — that is where the mail template engine
and the cache adapter live. A mechanism only *one* module needs stays in that module's own
`Common/`, and `InfrastructureModules_ReferenceOnlyPersistenceHorizontally` is what refuses the
third option of reaching sideways for it.

**A read that is expensive to repeat.** `ICacheStore`, from
`AppTemplate.Application.Core/Common/Ports/`, and the list of what may go through it is
short: an answer whose staleness is harmless. Nothing that decides an authorisation,
enforces a bound, becomes an `ETag`, or tells something else what to delete — a cached
bound is a bound multiplied by however many processes hold a copy, and a cached version is
a precondition that no longer describes the thing it guards. The worked example is
`GetUsedTodoItemTagsUseCase`, and it shows the whole shape: the key and the lifetime are
constants on one type in the business `Common/` (`UsedTagsCache`) rather than literals in
the use case, the key names the owner because the answer is theirs alone, and every
command that changes the answer calls `RemoveAsync` with the same key. An entry may vanish
before its lifetime is over, so the caller has to be correct when it does: the port
promises to be faster, never to remember.

**Work that runs on a timer.** A `BackgroundService` in `AppTemplate.Api/Features/<F>/Jobs/`,
started by an `AddXJobs()` extension beside it that `AddBackgroundJobs` calls — opening each pass
with `CreateBackgroundScope()`, so the scope has no caller and its writes record no user — and its
loop is `PeriodicJob.RunAsync` from `AppTemplate.Presentation.Core/Common/Jobs/` —
one interval, one asynchronous iteration, ticks coalesced so an iteration never overlaps
itself, and a return rather than a throw when the host stops. It is a primitive the
service composes, not a base class it derives from, so a feature needing three passes on
three intervals composes three of them; `FileBackgroundService` does exactly that. What
the primitive does not decide is what each loop still owes: its own start-up and shutdown
logs, its own span and counter names, its own DI scope per iteration, and its own
behaviour when its enabled flag is off — a disabled pass is logged and counted, because a
silent one is indistinguishable from a dead loop. Exclusivity between two processes is not
the loop's business: the operations that must not run twice at once take `ILeaderLease`
inside the use case, as `FireDueRemindersUseCase` and the three Files passes do.

## 5. Tests

`Tests/` mirrors `Src/`: domain tests under `Tests/Domain/AppTemplate.Domain.UnitTests/`,
application tests under `Tests/Application/AppTemplate.Application.UnitTests/`, and so
on. The mirror is exact down to `Features/<Feature>/`; below that it is followed only
where a feature has enough tests of one nature to be worth separating. In the
persistence mirror, for instance, the mapper and tracker tests sit directly under
`Features/<Feature>/` while `Access/Queries/` has a folder of its own. Match the neighbouring
feature rather than the source tree when the two disagree. Add:

- Domain: the aggregate's invariants, from the constructor, the factory, and
  `Rehydrate` alike.
- Application: one test class per use case, plus the validators.
- Persistence: a round-trip fidelity test for the mapper (`ToNewRecord` composed
  with `ToAggregate` must reproduce every property), and a tracker test.
- Presentation (`Tests/Presentation/AppTemplate.Api.UnitTests/Features/<Feature>/`): the
  controller and the request/response mapping, against test doubles.
- Integration (`Tests/Integration/AppTemplate.Api.IntegrationTests/<Feature>/`): the
  HTTP surface end to end, against a real PostgreSQL via Testcontainers.
  <!--#if (files)-->
- If the feature has a second store (section 4b), it needs integration tests against
  the real thing too. A signature is computed locally, so a wrong one is
  indistinguishable from a right one until something checks it —
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Storage/` is the worked example,
  against SeaweedFS under Testcontainers.
  <!--#endif-->

The round-trip fidelity test deserves a second mention when a feature has a second
store, because it stops being hygiene. With no tombstone, a mapper that writes a key
differing from the one the bytes went to turns a storage leak into **data loss**: the
sweep finds the object unreferenced and deletes a live file. `StoredFileMapperObjectKeyTests`
is that barrier, and the mapper says so in its own file.

A test that cannot fail is not a guarantee. For anything security- or
correctness-shaped: break the production code, watch the new test go red, then
restore it.

## 5b. The closed lists a new feature lands in

Nothing here is a hoop. Each of these is a list somebody has to change **on purpose**, and each
exists because a rule that discovers everything automatically also accepts everything
automatically. What follows is the set a feature actually reaches, so you meet them as a list rather
than one red test at a time.

**Measured rather than listed.** A generated feature composes with the six edits the generator
prints, and the layout and vocabulary rules stay green for it, because a feature that uses the shape's
own words is what they are checking for. Every combination of operations under each shape was built
that way with warnings as errors and run against the architecture rules. So the table below is what
fires **when you do the thing in the left column**, not a list of chores a feature owes.

| What you added | What has to say so |
|---|---|
| A registration a host must resolve | `Composition/ContainerCompositionTests.cs` and the five compositions in `Composition/HostComposition.cs`. These are the wiring, not a list. `ApplicationModuleTests` in the application unit tests names the calls, composes every feature, gives a double to each port, and for the rich shape counts the use cases in `_knownUseCaseCount` |
| An `IEntityTypeConfiguration` | `AppDbContext.OnModelCreating`, checked in both directions by `Rules/PersistenceModelTests.cs`. A configuration nobody applies is inert and produces an *empty* migration, so the failure would otherwise arrive at the first query |
| A **new word** for a folder under `Common/` or `Features/<F>/` | the two vocabularies in `Rules/LayoutConventionTests.cs`, checked in both directions — a word without its folder fails as loudly as a folder without its word. A feature using the existing words adds nothing here |
| A domain event | a consumer, **or** its name in `_deliberatelyUnconsumed` in `Rules/DomainEventTests.cs` with the reason. Publishing a fact nothing wants yet is good design; leaving a reader unable to tell that from a forgotten consumer is not |
| A public interface under `Common/` | it joins the port population by default. If it is not a port — something inside this layer implements it, not a module — it goes in `ApplicationPorts.NotPorts` with its reason, beside `ICollectionPolicy` and `ICollectionQuery` |
| An anonymous endpoint | `_anonymousActions` in `Rules/HttpSurfaceTests.cs` |
| A background service | the floor in `Rules/BackgroundWorkTests.cs`, and an instrument means `Rules/ObservabilityRegistrationTests.cs` |
<!--#if (templateMaintenance)-->
| A new project | guids in `AppTemplate.sln` **and** `.template.config/template.json` (`Rules/TemplatePackagingTests.cs` guards the list), a `COPY` line in both Dockerfiles, a mirror under `Tests/`, and the project floors in `Rules/ModuleDependencyTests.cs` |
<!--#else-->
| A new project | its entry in `AppTemplate.sln`, a `COPY` line in both Dockerfiles, a mirror under `Tests/`, and the project floors in `Rules/ModuleDependencyTests.cs` |
<!--#endif-->
| Anything at all, eventually | `coverage.minimum`, whose own rule says when the floor moves and demands Docker before it is touched |

`_expectedEntities` in `Rules/DomainModelTests.cs` and `Rules/FeatureFolderVocabularyTests.cs` are
deliberately **not** on that list. Both assert that what is known to be there is still there, so a
new aggregate or a new feature folder does not touch either — which is worth knowing, because the
obvious guess is the opposite.

**Two habits are worth more than the table.** Raise an anti-vacuity floor when you pass it, rather
than leaving it describing a smaller tree — a floor that no longer bites is a rule that has stopped
reading. And when you add to a hand-maintained list, ask which direction it cannot see: this suite
has been bitten twice by a list that checked words against folders and never folders against words.

## 6. Migration

```bash
dotnet run Tools/Tasks.cs migration-add <Name>
```

Then confirm nothing was missed:

```bash
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
```

`PendingModelChangesTests` asserts the same thing without a database and runs in CI, so
a migration that has fallen behind the model is caught whether or not anyone ran the
command. Read it together with the caution in section 3: it compares the model to the
snapshot, and a configuration that was never applied is missing from both, so a green
result here is not on its own evidence that your table exists.

[`MIGRATIONS.md`](MIGRATIONS.md) is the whole subject, both contexts: why the API applies migrations
at startup only in Development, and what a deployment does instead. `SECURITY.md` says what it owes
beyond the mechanics.

## 7. The simple shape — the `Notes` example
<!--#if (notes)-->
`Notes` has no domain folder at all. A note is a title and a body with an owner and a version, and
its only rules are limits on input: a title is required and bounded, a body is bounded. A limit on
input is validation, not state to protect, so the feature is a service over storage.

`Src/Application/AppTemplate.Application/Features/Notes/`

```
Model/Dtos/             NoteDto, NoteSummaryDto (what is read); NoteContentDto, NoteListQueryDto (what the
                        service is given); NoteContentDtoValidator, beside the Dto it validates
Model/Errors/           NoteErrors — note.notFound
Model/Policies/         NoteInputPolicy (the length limits), NoteCollectionPolicy (the sort whitelist)
Ports/NoteQueries/      INoteQueries, NoteFilter — the reads, projected to DTOs
Ports/NoteStore/        INoteStore, NoteChangeStatus — the writes, which only stage
Services/               INoteService, NoteService — one method per operation
Services/Consumers/     DeleteNotesOnAccountDeletedConsumer — a note leaves with its owner
```

- **One service, one method per operation** — `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`,
  `DeleteAsync` — behind an interface, because an entry point keeps its interface in both shapes.
  The service reads `ICurrentUser` itself and no method takes an owner, so no caller can name
  somebody else's. An anonymous caller is `auth.required` before storage is touched.
- **The owner never comes from the body.** `NoteContentDto` is the title and the body and nothing
  else; the id comes from the route and the owner from the caller.
- **A missing note and a foreign one are one answer.** Every `INoteStore` and `INoteQueries` method
  names the owner and scopes to it, so there is no row to read first and compare afterwards.
- **Validation is FluentValidation** on `NoteContentDto`, measured after trimming because that is
  what is stored. `ApplicationModule.AddFeature` discovers the validator by its namespace, as it
  does for every vertical.
- **Reads stage nothing.** The write half of storage is its own port because a port is one
  capability, and staging is not reading; `INoteStore` only stages, and the
  service commits through `IUnitOfWork`. The version is assigned by the database, so a write's
  answer is read back rather than assembled.

`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Notes/`

```
Rows/Models/NoteRecord.cs            the row, and the only model there is
Rows/Configurations/NoteRecordConfiguration.cs
Access/Queries/NoteQueries.cs, NoteSortMap.cs
Access/Stores/NoteStore.cs
```

`NoteRecord` is not `IAuditable`: the service sets the two instants, so the business context's audit
interceptor has nothing to stamp, and no tracker or event source holds a note, so its flush and
dispatch interceptors have nothing to act on either. `NoteStore` takes the `AppDbContext` and
nothing else — no mapper, no tracker. The edits in the context are the usual three (the
`NotesSchema` constant, the `Notes` `DbSet`, the `ApplyConfiguration` call), and
`PersistenceModule.AddNotesFeature` registers the store and the queries.

**How a stale write is refused.** The `xmin` column is the concurrency token. `INoteStore` loads the
owner's row tracked, compares the caller's `If-Match` against it (a stale one is
`ConcurrencyErrors.PreconditionFailed`, `412`) and stages the edit. EF then writes
`UPDATE … WHERE "Id" = @id AND xmin = @loaded`, and `DELETE` likewise. A competing writer that
commits between the load and the commit leaves no row at that `xmin`; the update touches nothing,
`EfUnitOfWork` turns EF's exception into `ConcurrencyConflictException`, and the API answers `409`
`concurrency.conflict`. That check is made by the database at the write, not by comparing against an
earlier read.

`Src/Presentation/AppTemplate.Api/Features/Notes/` is a controller, the request and response
contracts and a mapping, built on the same helpers as the others: `ETag` on a single read and on
every write's answer, `If-Match` on update and delete through `ReadPrecondition`, `[Idempotent]` on
the create only, and the complete `ProducesResponseType` set for the OpenAPI document.

**Composition is explicit, and it is three lines plus a test host.** `AddNotes()` in
`ApplicationModule`, called from `Program.cs`; `AddNotesFeature` inside `AddPersistenceModule`; and
`AddNotes()` in `Tests/Architecture/AppTemplate.Architecture.Tests/Composition/HostComposition.cs`
and `Tests/Presentation/AppTemplate.Api.UnitTests/Composition/SqliteSavingHost.cs`, which compose the
host the way `Program.cs` does.

**One word is this shape's own.** `Access/Stores/` (persistence) is the folder this shape needs and
the rich one does not, so it sits in the closed vocabulary in
`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/LayoutConventionTests.cs`; the validator
needs no word of its own, because with no operation folder to hold it, it files beside the Dto it
validates in `Model/Dtos/`. The rule that
every feature has a domain folder became
`FeatureIsolationTests.AFeatureWithoutADomainFolder_IsAServiceOverAStore`: the exemption exists only
for a service over its storage ports, a store to write and queries to read, that declares no use cases.

**What each write guarantees.**

| Write | Transaction | Concurrency | Idempotence | Retry |
|---|---|---|---|---|
| Create | One save of one row | None: nothing exists yet to have a version | `Idempotency-Key` replays the first response | Safe with a key; without one a repeat creates a second note |
| Update | One `UPDATE … WHERE xmin = @loaded` | `If-Match` stale is `412`; losing the race at the write is `409` | Same final state, with `If-Match` it applies once | The same `If-Match` is refused after the first success |
| Delete | One `DELETE … WHERE xmin = @loaded` | As update | Same final state | A repeat is `404` |

**Tests.** `Tests/Application/AppTemplate.Application.UnitTests/Features/Notes/` proves the matrix
once for all five operations (ownership, not-found against foreign, validation, the conflict
passing through). `Tests/Presentation/AppTemplate.Api.UnitTests/Composition/NotesSavingTests.cs` runs
the real module composition on SQLite, with `xmin` played by an integer column, and shows that a
note commits through the unit of work alone and that a stale write is refused at the write.
`Tests/Integration/AppTemplate.Api.IntegrationTests/Notes/` drives CRUD, cross-user isolation, the
stale and competing writes and the idempotent create through the real API against PostgreSQL.

<!--#else

This project was generated without the `Notes` example (`--examples none`). The simple shape is a
service over two storage ports: one service with a method per operation, a write port
(`I<Feature>Store`) and a read port (`I<Feature>Queries`) in the application layer, an EF record that
is the whole model in persistence, no domain folder, its input's validator beside that Dto in
`Model/Dtos/`, and its store adapter in `Access/Stores/`. `new-feature <Features> <Type> --model record`
writes it; a project generated with `--examples notes` or `all` holds the worked example this section
walks through.

#endif-->
## 8. From simple to rich — the promotion path

A note that acquires a rule — at most a hundred notes per owner, or a note that cannot be edited once
archived — now has a state to protect. The way across is the only bridge between the shapes, and it
changes the inside of the feature, not its surface:

1. **Extract the aggregate.** `Note` in the domain's `Model/Entities/`, with a factory and a rehydration
   path that run the same invariants, its value objects, and `INoteRepository` in `Repositories/`
   (section 1).
2. **Replace the store by a repository.** `NoteRecord` stays, since it is already the row; add the
   mapper and the tracker and register them the way `AddTodoListsFeature` does (section 3). The write
   half of `INoteStore` moves into the repository and the port goes; `INoteQueries` stays as the read
   port.
3. **Split the service into use cases.** One folder per operation with its command, validator and
   named interface (section 2). Keep a service only if the operations share a gate, as
   `ITodoListService` does for loading an owned aggregate.
4. **Point the controller at the use cases.**

Routes, contracts and responses do not change, and neither do `ETag`, `If-Match`, `Idempotency-Key`
or the owner scoping: none of them was ever the aggregate's. The migration is empty unless the new
rule needs a column or an index.

## Removing the sample instead

**At generation, `--examples` decides.** `dotnet new cleanarch-webapi --examples notes` starts a
project with `Notes` only and `--examples none` with no example at all; see
[`USING-THE-TEMPLATE.md`](USING-THE-TEMPLATE.md#generation-options). What that costs is stated
there: several tests use the to-do list endpoints as their subject for cross-cutting concerns
(ownership isolation, conditional requests, idempotent replay, auditing) that have nothing to do
with to-do lists themselves, and they leave with the example rather than being rewritten against a
different resource.

**In a project generated with the examples**, follow `docs/REMOVING-THE-EXAMPLE-FEATURES.md` rather
than improvising from the tree. Deleting the folders is the easy half. The removal takes
`Reminders` with it, because a reminder is scheduled against a to-do item and that
feature does not compile without this one; and the migration is where guessing costs
data, since the business context's one initial migration carries `todo`, `reminders` and `files`
together and what to do with it depends on whether a database has already applied it. That document names every file, every edit and the state each
test suite is left in.
